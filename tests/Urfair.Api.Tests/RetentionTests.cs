using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urfair.Api.Data;

namespace Urfair.Api.Tests;

/// <summary>Automatic deletion after a vote ended (Retention.cs) and the legal pages' texts.</summary>
public class RetentionTests
{
    const string David = "David Doppler";

    static async Task SetPhaseAsync(HttpClient organiser, Guid slot, string phase)
    {
        var body = JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
        body["phase"] = phase;
        Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body)).StatusCode);
    }

    static async Task<JsonElement> SlotAsync(HttpClient organiser, Guid slot) =>
        await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}");

    static string? DeleteOn(JsonElement slot) =>
        slot.TryGetProperty("deleteOn", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

    [Fact]
    public async Task Ending_a_vote_starts_the_deletion_period_and_going_back_stops_it()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        Assert.Null(DeleteOn(await SlotAsync(organiser, slot)));

        await SetPhaseAsync(organiser, slot, "Ended");
        var expected = DateOnly.FromDateTime(Phases.Now().AddDays(Retention.DefaultDays)).ToString("yyyy-MM-dd");
        Assert.Equal(expected, DeleteOn(await SlotAsync(organiser, slot)));
        var mine = await (await app.CreateClient().PostAsJsonAsync("/api/my-slots", new { name = David })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, DeleteOn(mine.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == slot)));

        await SetPhaseAsync(organiser, slot, "Closed");
        Assert.Null(DeleteOn(await SlotAsync(organiser, slot)));
    }

    [Fact]
    public async Task A_vote_is_deleted_with_all_its_data_when_the_period_is_over()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var holiday = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var excursion = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
        await SetPhaseAsync(organiser, holiday, "Ended");
        await SetPhaseAsync(organiser, excursion, "Ended");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Ballots.AnyAsync(b => b.FairSlotId == holiday));
        Assert.True(await db.AllocationLog.AnyAsync(l => l.FairSlotId == holiday));
        (await db.FairSlots.SingleAsync(s => s.Id == holiday)).EndedAt = Phases.Now().AddDays(-Retention.DefaultDays - 1);
        (await db.FairSlots.SingleAsync(s => s.Id == excursion)).EndedAt = Phases.Now().AddDays(-Retention.DefaultDays + 1);
        await db.SaveChangesAsync();

        var deleted = await Retention.DeleteExpiredAsync(db, Retention.DefaultDays);

        Assert.Equal(["Ferienprogramm"], deleted);
        Assert.False(await db.FairSlots.AnyAsync(s => s.Id == holiday));
        Assert.False(await db.Ballots.AnyAsync(b => b.FairSlotId == holiday));
        Assert.False(await db.Participants.AnyAsync(p => p.FairSlotId == holiday));
        Assert.False(await db.Activities.AnyAsync(a => a.FairSlotId == holiday));
        Assert.False(await db.Assignments.AnyAsync(a => a.FairSlotId == holiday));
        Assert.False(await db.AllocationLog.AnyAsync(l => l.FairSlotId == holiday));
        Assert.False(await db.AllocationSnapshots.AnyAsync(s => s.FairSlotId == holiday));
        Assert.False(await db.Mails.AnyAsync(m => m.FairSlotId == holiday));
        Assert.False(await db.SeatOffers.AnyAsync(o => o.FairSlotId == holiday));
        // Ended a day too late for deletion: still there.
        Assert.True(await db.Ballots.AnyAsync(b => b.FairSlotId == excursion));
    }

    [Fact]
    public async Task The_legal_texts_come_from_the_configuration()
    {
        using var app = new DemoApp();
        var plain = await app.CreateClient().GetFromJsonAsync<JsonElement>("/api/legal");
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("imprint").ValueKind);
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("privacy").ValueKind);
        Assert.Equal(Retention.DefaultDays, plain.GetProperty("retentionDays").GetInt32());

        using var configured = app.WithWebHostBuilder(b => b.UseSetting("Legal:Privacy", "## Verantwortlich\nPfarre").UseSetting("Retention:Days", "30"));
        var legal = await configured.CreateClient().GetFromJsonAsync<JsonElement>("/api/legal");
        Assert.Equal("## Verantwortlich\nPfarre", legal.GetProperty("privacy").GetString());
        Assert.Equal(30, legal.GetProperty("retentionDays").GetInt32());
    }
}
