using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urfair.Api.Data;

namespace Urfair.Api.Tests;

/// <summary>The seeded demo mail traffic and the changes to leaders' lists, mailed when the organisers decide (LeaderNotices.cs).</summary>
public class LeaderNoticeTests
{
    const string Ben = "Ben Berger", Clara = "Clara Czerny", David = "David Doppler", Kochen = "Kochen mit Oma";
    const string Hermine = "hermine.gruber@example.org", Lukas = "lukas.mayr@example.org", Stefan = "stefan.huber@example.org";

    sealed record Mail(string To, string? From, string Subject, string Body);

    static async Task<List<Mail>> MailsAsync(HttpClient organiser, Guid slot) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/mails")).EnumerateArray()
            .Select(m => new Mail(m.GetProperty("to").GetString()!, m.GetProperty("from").GetString(), m.GetProperty("subject").GetString()!,
                m.GetProperty("body").GetString()!)).ToList();

    static List<Mail> Changes(List<Mail> mails, string to) =>
        mails.Where(m => m.To == to && m.Subject.StartsWith("Änderung Teilnahmeliste")).ToList();

    static async Task<string> ActivityIdAsync(HttpClient client, Guid slot, string person, string title) =>
        DemoApp.Activity(await DemoApp.StatusAsync(client, slot, person), title).GetProperty("activityId").GetString()!;

    static async Task<string> OfferIdAsync(HttpClient client, Guid slot, string person) =>
        (await DemoApp.StatusAsync(client, slot, person)).GetProperty("offer").GetProperty("id").GetString()!;

    [Fact]
    public async Task The_demo_starts_with_invitations_results_and_lists_sent_and_shows_who_sent_what()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var client = app.CreateClient();

        var mails = await MailsAsync(organiser, slot);

        Assert.Equal(3, mails.Count(m => m.Subject.StartsWith("Deine Anmeldung")));
        Assert.Equal(3, mails.Count(m => m.Subject.StartsWith("Deine Zuteilung")));
        Assert.Equal([Hermine, Lukas], mails.Where(m => m.Subject.StartsWith("Teilnahmeliste")).Select(m => m.To).Order());
        Assert.All(mails, m => Assert.Equal(Stefan, m.From));
        Assert.Empty(Changes(mails, Hermine)); // the seeded lists are up to date

        (await DemoApp.GiveBackAsync(client, slot, David, await ActivityIdAsync(client, slot, David, Kochen), replacement: true)).EnsureSuccessStatusCode();

        var cancelled = (await MailsAsync(organiser, slot)).Single(m => m.Subject.StartsWith("Absage"));
        Assert.Equal((Stefan, "david.doppler@example.org"), (cancelled.To, cancelled.From));
        var boxes = (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/mailboxes")).EnumerateArray()
            .Select(b => (b.GetProperty("address").GetString(), string.Join(",", b.GetProperty("roles").EnumerateArray().Select(r => r.GetString())))).ToList();
        Assert.Equal([(Stefan, "organiser"), (Hermine, "leader"), (Lukas, "leader"), ("ben.berger@example.org", "participant"),
            ("clara.czerny@example.org", "participant"), ("david.doppler@example.org", "participant")], boxes);
    }

    [Fact]
    public async Task The_organiser_who_also_leads_has_one_mailbox_with_both_roles()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");

        var miriam = (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/mailboxes")).EnumerateArray()
            .Single(b => b.GetProperty("address").GetString() == "miriam.koller@example.org");

        Assert.Equal(["organiser", "leader"], miriam.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    static async Task<List<JsonElement>> PendingAsync(HttpClient organiser, Guid slot) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/leader-changes")).EnumerateArray().ToList();

    static JsonElement For(List<JsonElement> pending, string email) => pending.Single(p => p.GetProperty("email").GetString() == email);

    static List<string> Names(JsonElement session, string list) => session.GetProperty(list).EnumerateArray().Select(x => x.GetString()!).ToList();

    static async Task<int> SendAsync(HttpClient organiser, Guid slot, params string[] emails)
    {
        var response = await organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/leader-changes/send", new { emails });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32();
    }

    [Fact]
    public async Task When_David_cancels_Hermine_is_only_mailed_once_the_organisers_decide()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var client = app.CreateClient();

        (await DemoApp.GiveBackAsync(client, slot, David, await ActivityIdAsync(client, slot, David, Kochen), replacement: true)).EnsureSuccessStatusCode();

        Assert.Empty(Changes(await MailsAsync(organiser, slot), Hermine));
        var pending = await PendingAsync(organiser, slot);
        var session = Assert.Single(For(pending, Hermine).GetProperty("sessions").EnumerateArray());
        Assert.Equal((Kochen, 7, 8), (session.GetProperty("title").GetString(), session.GetProperty("count").GetInt32(), session.GetProperty("capacity").GetInt32()));
        Assert.Equal([David], Names(session, "removed"));
        Assert.Empty(Names(session, "added"));
        Assert.DoesNotContain(pending, p => p.GetProperty("email").GetString() == Lukas); // David keeps Waldtag

        Assert.Equal(1, await SendAsync(organiser, slot, Hermine));

        var change = Assert.Single(Changes(await MailsAsync(organiser, slot), Hermine));
        Assert.Contains("Nicht mehr dabei: David Doppler", change.Body);
        Assert.Contains("Jetzt 7 von 8 Plätzen", change.Body);
        Assert.DoesNotContain("Neu dabei", change.Body);
        Assert.Empty(await PendingAsync(organiser, slot));
        Assert.Equal(0, await SendAsync(organiser, slot));
    }

    [Fact]
    public async Task Clara_declines_Ben_accepts_and_gets_a_confirmation_while_both_leaders_wait_for_the_organisers()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var client = app.CreateClient();
        (await DemoApp.GiveBackAsync(client, slot, David, await ActivityIdAsync(client, slot, David, Kochen), replacement: true)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/slots/{slot}/offers/{await OfferIdAsync(client, slot, Clara)}/decline", new { name = Clara })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/slots/{slot}/offers/{await OfferIdAsync(client, slot, Ben)}/accept", new { name = Ben })).EnsureSuccessStatusCode();

        var mails = await MailsAsync(organiser, slot);
        var confirmed = mails.Single(m => m.Subject.StartsWith("Platz bestätigt"));
        Assert.Equal("ben.berger@example.org", confirmed.To);
        Assert.Contains("Waldtag mit dem Förster hast du dafür abgegeben", confirmed.Body);
        Assert.Contains(mails, m => m.To == "clara.czerny@example.org" && m.Subject.StartsWith("Freier Platz für dich"));
        Assert.Contains(mails, m => m.To == "ben.berger@example.org" && m.Subject.StartsWith("Freier Platz für dich"));
        Assert.Equal([Hermine, Lukas], (await PendingAsync(organiser, slot)).Select(p => p.GetProperty("email").GetString()).Order());

        Assert.Equal(2, await SendAsync(organiser, slot));

        mails = await MailsAsync(organiser, slot);
        Assert.Contains("Neu dabei: Ben Berger", Assert.Single(Changes(mails, Hermine)).Body);
        Assert.Contains("Nicht mehr dabei: Ben Berger", Assert.Single(Changes(mails, Lukas)).Body);
    }

    [Fact]
    public async Task When_Clara_moves_up_in_the_excursion_Miriam_can_be_told()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
        var client = app.CreateClient();
        var kletterwald = await ActivityIdAsync(client, slot, Clara, "Kletterwald Linz");
        (await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = new[] { kletterwald } })).EnsureSuccessStatusCode();

        (await DemoApp.GiveBackAsync(client, slot, David, kletterwald, replacement: false)).EnsureSuccessStatusCode();
        Assert.Equal(1, await SendAsync(organiser, slot, "MIRIAM.koller@example.org"));

        var change = Assert.Single(Changes(await MailsAsync(organiser, slot), "miriam.koller@example.org"));
        Assert.Contains("Neu dabei: Clara Czerny", change.Body);
        Assert.Contains("Nicht mehr dabei: David Doppler", change.Body);
    }

    [Fact]
    public async Task Changes_by_hand_are_never_mailed_on_their_own()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // As if Xaver had been moved out of "Kochen mit Oma" by hand after the lists went out: the list still names him.
        var activity = await db.Activities.SingleAsync(a => a.FairSlotId == slot && a.Title == Kochen);
        var kochen = await db.Sessions.SingleAsync(s => s.ActivityId == activity.Id);
        kochen.LeaderList = LeaderNotices.ListText(kochen.LeaderList!.Split('\n').Append("Xaver Example"));
        await db.SaveChangesAsync();
        foreach (var entry in await db.AllocationLog.Where(l => l.FairSlotId == slot).ToListAsync()) entry.At = entry.At.AddHours(-1);
        await db.SaveChangesAsync();

        await Phases.TickAsync(db);

        Assert.Empty(Changes(await MailsAsync(organiser, slot), Hermine));
        var session = Assert.Single(For(await PendingAsync(organiser, slot), Hermine).GetProperty("sessions").EnumerateArray());
        Assert.Equal(["Xaver Example"], Names(session, "removed"));
    }
}