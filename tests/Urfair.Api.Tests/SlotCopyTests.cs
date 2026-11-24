using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urfair.Api.Data;

namespace Urfair.Api.Tests;

/// <summary>A new vote from an existing one as a template (SlotCopy.cs).</summary>
public class SlotCopyTests
{
    static async Task<HttpResponseMessage> CopyAsync(HttpClient organiser, Guid source, string name, int shiftWeeks = 52) =>
        await organiser.PostAsJsonAsync("/api/admin/slots", new { name, withSampleData = false, copyFrom = source, shiftWeeks });

    static async Task<Guid> NewIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    static async Task<HttpClient> AccountAsync(DemoApp app, string key)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/demo", new { key })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task An_ended_vote_is_copied_with_its_activities_and_dates_a_year_later_but_without_people()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var source = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var settings = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{source}");
        var body = System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText())!;
        body["phase"] = "Ended";
        Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{source}", body)).StatusCode);

        var copy = await NewIdAsync(await CopyAsync(organiser, source, "Ferienprogramm 2028"));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (old, created) = (await db.FairSlots.SingleAsync(s => s.Id == source), await db.FairSlots.SingleAsync(s => s.Id == copy));
        Assert.Equal("Ferienprogramm 2028", created.Name);
        Assert.Equal(Phase.Draft, created.Phase);
        Assert.Null(created.FrozenAt);
        Assert.Null(created.EndedAt);
        Assert.Equal(old.ToRules(), created.ToRules());
        Assert.Equal((old.Parish, old.Organiser, old.OrganiserEmail, old.Visibility), (created.Parish, created.Organiser, created.OrganiserEmail, created.Visibility));
        Assert.Equal(old.RegistrationDeadline!.Value.AddDays(364), created.RegistrationDeadline);

        var before = await db.Activities.Include(a => a.Sessions).Where(a => a.FairSlotId == source).OrderBy(a => a.SortOrder).ToListAsync();
        var after = await db.Activities.Include(a => a.Sessions).Where(a => a.FairSlotId == copy).OrderBy(a => a.SortOrder).ToListAsync();
        Assert.Equal(before.Select(a => (a.Title, a.GroupId, a.Leader, a.LeaderEmail)), after.Select(a => (a.Title, a.GroupId, a.Leader, a.LeaderEmail)));
        Assert.Equal(before.Select(a => a.Sessions.Select(s => (s.Start.AddDays(364), s.Location, s.Capacity)).Order()),
            after.Select(a => a.Sessions.Select(s => (s.Start, s.Location, s.Capacity)).Order()));
        Assert.Empty(before.Select(a => a.Id).Intersect(after.Select(a => a.Id)));

        Assert.False(await db.Participants.AnyAsync(p => p.FairSlotId == copy));
        Assert.False(await db.Ballots.AnyAsync(b => b.FairSlotId == copy));
        Assert.True(await db.Participants.AnyAsync(p => p.FairSlotId == source));
        Assert.Equal(before.Count, await db.Activities.CountAsync(a => a.FairSlotId == source));
    }

    [Fact]
    public async Task A_parish_admin_copies_only_what_they_see_into_their_own_parish()
    {
        using var app = new DemoApp();
        var junia = await app.OrganiserAsync();
        var excursion = await DemoApp.SlotIdAsync(junia, "Ministrantenausflug");
        var firmung = await DemoApp.SlotIdAsync(junia, "Firmvorbereitung");
        var festival = await DemoApp.SlotIdAsync(junia, "Pfarrfest");
        var markus = await AccountAsync(app, "markus");

        Assert.Equal(HttpStatusCode.NotFound, (await CopyAsync(markus, excursion, "Ausflug")).StatusCode);
        var fromJunia = await NewIdAsync(await CopyAsync(markus, firmung, "Firmung St. Markus", 0));
        var ownFestival = await NewIdAsync(await CopyAsync(markus, festival, "Pfarrfest 2028"));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await db.FairSlots.SingleAsync(s => s.Id == fromJunia);
        Assert.Equal(("Pfarrgemeinde Urfahr-St. Markus", Visibility.ParishOnly), (a.Parish, a.Visibility));
        var b = await db.FairSlots.SingleAsync(s => s.Id == ownFestival);
        Assert.Equal(("Pfarrgemeinde Urfahr-St. Markus", Visibility.ParishOnly), (b.Parish, b.Visibility));
        Assert.True(b.OpenToAll);
    }

    [Fact]
    public async Task Dates_move_by_at_most_two_years()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var source = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");
        var response = await CopyAsync(organiser, source, "Pfarrfest", 105);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("shiftWeeks", await response.Content.ReadAsStringAsync());
    }
}
