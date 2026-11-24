using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urfair.Api.Data;

namespace Urfair.Api.Tests;

/// <summary>Filling a seat after the lists are final, in the demo "Ferienprogramm" (SeatOffers.cs).</summary>
public class SeatOfferTests
{
    const string Ben = "Ben Berger", Clara = "Clara Czerny", David = "David Doppler", Kochen = "Kochen mit Oma", Waldtag = "Waldtag mit dem Förster";

    static async Task<JsonElement> PersonAsync(HttpClient organiser, Guid slot, string name) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation")).GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == name);

    static List<string> Seats(JsonElement person) =>
        person.GetProperty("assignments").EnumerateArray().Select(a => a.GetProperty("title").GetString()!).ToList();

    static async Task<string> SessionAsync(HttpClient organiser, Guid slot) =>
        (await PersonAsync(organiser, slot, David)).GetProperty("assignments").EnumerateArray()
            .Single(a => a.GetProperty("title").GetString() == Kochen).GetProperty("sessionId").GetString()!;

    /// <summary>David cancels "Kochen mit Oma"; returns the slot, a participant client and the session of his seat.</summary>
    static async Task<(Guid Slot, HttpClient Client, string Session)> DavidCancelsAsync(HttpClient organiser, DemoApp app, bool autoFill = true)
    {
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        if (!autoFill) await AutoFillAsync(organiser, slot, false);
        var session = await SessionAsync(organiser, slot);
        var client = app.CreateClient();
        var kochen = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, David), Kochen).GetProperty("activityId").GetString()!;
        (await DemoApp.GiveBackAsync(client, slot, David, kochen, replacement: true)).EnsureSuccessStatusCode();
        return (slot, client, session);
    }

    static async Task AutoFillAsync(HttpClient organiser, Guid slot, bool on)
    {
        var body = JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
        body["autoFill"] = on;
        Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body)).StatusCode);
    }

    static Task<HttpResponseMessage> FillAsync(HttpClient organiser, Guid slot, string session) =>
        organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/offers", new { sessionId = session });

    static Task<HttpResponseMessage> AnswerAsync(HttpClient client, Guid slot, string offer, string answer, string name = Clara) =>
        client.PostAsJsonAsync($"/api/slots/{slot}/offers/{offer}/{answer}", new { name });

    static async Task<string> OfferIdAsync(HttpClient client, Guid slot, string name) =>
        (await DemoApp.StatusAsync(client, slot, name)).GetProperty("offer").GetProperty("id").GetString()!;

    static async Task<string> MailBodyAsync(HttpClient organiser, Guid slot, string subjectStart) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/mails")).EnumerateArray()
            .First(m => m.GetProperty("subject").GetString()!.StartsWith(subjectStart)).GetProperty("body").GetString()!;

    static async Task<List<string>> MyMailsAsync(HttpClient client, Guid slot, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/slots/{slot}/my-mails", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(m => m.GetProperty("subject").GetString()!).ToList();
    }

    [Fact]
    public async Task Davids_seat_is_offered_to_Clara_at_once_and_after_she_accepts_David_gets_suggestions()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var (slot, client, session) = await DavidCancelsAsync(organiser, app);
        Assert.Equal(["Parkbad-Nachmittag", "Fußballturnier"], Seats(await PersonAsync(organiser, slot, Clara)));

        var offer = (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer");
        Assert.Equal(Kochen, offer.GetProperty("title").GetString());
        Assert.Equal("Fußballturnier", offer.GetProperty("givesUp").GetString());
        Assert.Contains(await DemoApp.MailSubjectsAsync(organiser, slot), s => s.StartsWith("Freier Platz für dich"));
        Assert.Contains("automatisch der nächsten Person", await MailBodyAsync(organiser, slot, "Absage: David"));
        Assert.Contains("noFreeSeat", await (await FillAsync(organiser, slot, session)).Content.ReadAsStringAsync()); // held for Clara

        var accepted = await AnswerAsync(client, slot, offer.GetProperty("id").GetString()!, "accept");

        accepted.EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("offer").ValueKind);
        Assert.Equal([Kochen, "Parkbad-Nachmittag"], Seats(await PersonAsync(organiser, slot, Clara)));
        var log = await DemoApp.LogAsync(organiser, slot);
        Assert.Contains(("offered", Clara), log);
        Assert.Contains(("offerAccepted", Clara), log);
        Assert.Equal("offerNone", log[0].Kind); // nobody else wants Fußballturnier
        var david = await PersonAsync(organiser, slot, David);
        Assert.True(david.GetProperty("needsReplacement").GetBoolean());
        var suggestions = david.GetProperty("suggestions").EnumerateArray()
            .Select(s => (s.GetProperty("title").GetString(), s.GetProperty("kind").GetString())).ToList();
        Assert.Contains(("Fußballturnier", "free"), suggestions);
        Assert.Contains(("Kreativwerkstatt", "overbook"), suggestions);
    }

    [Fact]
    public async Task Without_automatic_filling_the_preview_shows_Clara_and_the_organisers_start_the_offer()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var (slot, client, session) = await DavidCancelsAsync(organiser, app, autoFill: false);
        Assert.Equal(JsonValueKind.Null, (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer").ValueKind);
        Assert.Contains("rückt niemand von selbst nach", await MailBodyAsync(organiser, slot, "Absage: David"));

        var preview = (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/offers/next")).EnumerateArray()
            .Single(p => p.GetProperty("sessionId").GetString() == session).GetProperty("next").EnumerateArray().ToList();
        Assert.Equal(Clara, preview[0].GetProperty("person").GetString());
        Assert.Equal("Fußballturnier", preview[0].GetProperty("givesUp").GetString());
        Assert.DoesNotContain(preview, p => p.GetProperty("person").GetString() == David); // cancelled, never again

        Assert.Equal(HttpStatusCode.NoContent, (await FillAsync(organiser, slot, session)).StatusCode);

        Assert.Equal(Kochen, (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer").GetProperty("title").GetString());
        var after = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/offers/next");
        Assert.DoesNotContain(after.EnumerateArray(), p => p.GetProperty("sessionId").GetString() == session); // the free seat is held
    }

    [Fact]
    public async Task When_closed_Clara_can_remove_her_tick_but_not_add_one_and_Ben_is_asked_instead()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var client = app.CreateClient();
        var kochen = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, Clara), Kochen);
        Assert.True(kochen.GetProperty("waitlisted").GetBoolean());
        var kochenId = kochen.GetProperty("activityId").GetString()!;

        (await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = Array.Empty<string>() })).EnsureSuccessStatusCode();
        var again = await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = new[] { kochenId } });

        Assert.Contains("waitlistClosed", await again.Content.ReadAsStringAsync());
        Assert.False(DemoApp.Activity(await DemoApp.StatusAsync(client, slot, Clara), Kochen).GetProperty("waitlisted").GetBoolean());
        var david = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, David), Kochen).GetProperty("activityId").GetString()!;
        (await DemoApp.GiveBackAsync(client, slot, David, david, replacement: false)).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer").ValueKind);
        var offer = (await DemoApp.StatusAsync(client, slot, Ben)).GetProperty("offer");
        Assert.Equal(Kochen, offer.GetProperty("title").GetString());
        Assert.Equal(Waldtag, offer.GetProperty("givesUp").GetString());
    }

    [Fact]
    public async Task In_the_demo_participants_see_only_their_own_mails()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var (slot, client, _) = await DavidCancelsAsync(organiser, app);

        var clara = await MyMailsAsync(client, slot, Clara);
        var david = await MyMailsAsync(client, slot, David);

        Assert.Contains(clara, s => s.StartsWith("Freier Platz für dich"));
        Assert.DoesNotContain(clara, s => s.StartsWith("Absage")); // to the organisers
        Assert.DoesNotContain(david, s => s.StartsWith("Freier Platz für dich"));
    }

    [Theory]
    [InlineData(10, 48)]
    [InlineData(6, 24)]
    [InlineData(2, 12)]
    public void The_closer_the_date_the_shorter_the_time_to_answer(int daysAhead, int hours)
    {
        var now = new DateTime(2027, 7, 1, 12, 0, 0);
        Assert.Equal(TimeSpan.FromHours(hours), SeatOffers.AnswerTimeFor(now.AddDays(daysAhead), now));
    }

    [Fact]
    public async Task A_declined_offer_goes_to_the_next_person_and_seats_stay()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var (slot, client, session) = await DavidCancelsAsync(organiser, app);
        var offer = await OfferIdAsync(client, slot, Clara);

        (await AnswerAsync(client, slot, offer, "decline")).EnsureSuccessStatusCode();

        Assert.Equal(["Parkbad-Nachmittag", "Fußballturnier"], Seats(await PersonAsync(organiser, slot, Clara)));
        var log = await DemoApp.LogAsync(organiser, slot);
        Assert.Contains(("offerDeclined", Clara), log);
        Assert.Equal(("offered", Ben), log[0]);
        Assert.Equal(HttpStatusCode.BadRequest, (await AnswerAsync(client, slot, offer, "accept")).StatusCode);

        (await AnswerAsync(client, slot, await OfferIdAsync(client, slot, Ben), "decline", Ben)).EnsureSuccessStatusCode();

        Assert.Equal("offerNone", (await DemoApp.LogAsync(organiser, slot))[0].Kind);
        Assert.Contains("nobodyLeft", await (await FillAsync(organiser, slot, session)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_expired_offer_is_mailed_and_nobody_is_asked_twice()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var (slot, client, session) = await DavidCancelsAsync(organiser, app);
        async Task ExpireAsync()
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.SeatOffers.SingleAsync(o => o.Status == OfferStatus.Open)).ExpiresAt = Phases.Now().AddMinutes(-1);
            await db.SaveChangesAsync();
            await Phases.TickAsync(db);
        }

        await ExpireAsync();

        Assert.Contains(("offerExpired", Clara), await DemoApp.LogAsync(organiser, slot));
        Assert.Contains(await DemoApp.MailSubjectsAsync(organiser, slot), s => s.StartsWith("Angebot abgelaufen"));
        Assert.Equal(JsonValueKind.Null, (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer").ValueKind);
        Assert.Equal(Kochen, (await DemoApp.StatusAsync(client, slot, Ben)).GetProperty("offer").GetProperty("title").GetString());

        // Ben lets it expire too; a new round may ask only people not yet asked, so nobody is left.
        await ExpireAsync();
        Assert.Contains(("offerExpired", Ben), await DemoApp.LogAsync(organiser, slot));
        Assert.Contains("nobodyLeft", await (await FillAsync(organiser, slot, session)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Leaving_closed_withdraws_an_open_offer()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var (slot, _, _) = await DavidCancelsAsync(organiser, app);

        var body = JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
        body["phase"] = "Published";
        Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body)).StatusCode);

        Assert.Contains(("offerWithdrawn", Clara), await DemoApp.LogAsync(organiser, slot));
        var offers = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/offers");
        Assert.Equal("withdrawn", offers.EnumerateArray().Single().GetProperty("status").GetString());
    }
}
