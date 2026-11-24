using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Urfair.Api.Tests;

/// <summary>The whole app with a fresh demo database (SQLite file in the temp folder) per instance.</summary>
public sealed class DemoApp : WebApplicationFactory<Program>
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"urfair-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={file}");
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("DATABASE_URL", "");
        builder.UseSetting("Database:Provider", "Sqlite");
    }

    /// <summary>A client signed in as the demo organisation account (Junia team).</summary>
    public async Task<HttpClient> OrganiserAsync()
    {
        var client = CreateClient();
        (await client.PostAsJsonAsync("/api/auth/demo", new { key = "junia" })).EnsureSuccessStatusCode();
        Assert.True(File.Exists(file), "The test must use its own database file.");
        return client;
    }

    public static async Task<Guid> SlotIdAsync(HttpClient organiser, string name)
    {
        var slots = await organiser.GetFromJsonAsync<JsonElement>("/api/admin/slots");
        return slots.EnumerateArray().Single(s => s.GetProperty("name").GetString()!.Contains(name)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> StatusAsync(HttpClient client, Guid slot, string person)
    {
        var response = await client.PostAsJsonAsync($"/api/slots/{slot}/my-status", new { name = person });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static JsonElement Activity(JsonElement status, string title) =>
        status.GetProperty("activities").EnumerateArray().Single(a => a.GetProperty("title").GetString() == title);

    public static async Task<List<string>> MailSubjectsAsync(HttpClient organiser, Guid slot) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/mails")).EnumerateArray()
            .Select(m => m.GetProperty("subject").GetString()!).ToList();

    public static async Task<List<(string Kind, string? Person)>> LogAsync(HttpClient organiser, Guid slot) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation/log")).EnumerateArray()
            .Select(l => (l.GetProperty("kind").GetString()!, l.GetProperty("person").GetString())).ToList();

    public static Task<HttpResponseMessage> GiveBackAsync(HttpClient client, Guid slot, string person, string activityId, bool replacement) =>
        client.PostAsJsonAsync($"/api/slots/{slot}/give-back", new { name = person, activityId, replacement });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { file, file + "-wal", file + "-shm" })
            try { File.Delete(f); } catch (IOException) { /* still locked: the temp folder is cleaned up eventually */ }
    }
}

/// <summary>The demo workflows from the start page, end to end through the API (seed in Database.cs and DemoData.cs).</summary>
public class DemoWorkflowTests
{
    const string Ben = "Ben Berger", Clara = "Clara Czerny", David = "David Doppler";

    [Fact]
    public async Task Excursion_is_full_and_Ben_sits_in_his_weakest_wish_with_swap_offers()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");

        var ben = await DemoApp.StatusAsync(app.CreateClient(), slot, Ben);

        Assert.Equal("fixed", DemoApp.Activity(ben, "Ars Electronica Center").GetProperty("status").GetString());
        Assert.Empty(ben.GetProperty("missing").EnumerateArray());
        var tiergarten = DemoApp.Activity(ben, "Tiergarten Walding");
        Assert.True(tiergarten.GetProperty("canMoveUp").GetBoolean());
        Assert.Equal("Ars Electronica Center", tiergarten.GetProperty("givesUp").GetString());
    }

    [Fact]
    public async Task Ben_giving_back_without_a_free_seat_is_left_short_and_the_organisers_are_told()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
        var client = app.CreateClient();
        var ars = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, Ben), "Ars Electronica Center").GetProperty("activityId").GetString()!;

        (await DemoApp.GiveBackAsync(client, slot, Ben, ars, replacement: true)).EnsureSuccessStatusCode();

        var ben = await DemoApp.StatusAsync(client, slot, Ben);
        Assert.Equal("givenBack", DemoApp.Activity(ben, "Ars Electronica Center").GetProperty("status").GetString());
        var missing = Assert.Single(ben.GetProperty("missing").EnumerateArray());
        Assert.Equal(1, missing.GetProperty("count").GetInt32());
        Assert.Empty(missing.GetProperty("open").EnumerateArray());
        Assert.Contains(await DemoApp.MailSubjectsAsync(organiser, slot), s => s.StartsWith("Platz fehlt: " + Ben));
        Assert.Contains(("givenBack", Ben), await DemoApp.LogAsync(organiser, slot));
    }

    [Fact]
    public async Task Clara_with_a_tick_moves_up_when_David_gives_back_Kletterwald()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
        var client = app.CreateClient();
        var kletterwald = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, Clara), "Kletterwald Linz").GetProperty("activityId").GetString()!;

        (await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = new[] { kletterwald } })).EnsureSuccessStatusCode();
        (await DemoApp.GiveBackAsync(client, slot, David, kletterwald, replacement: false)).EnsureSuccessStatusCode();

        var clara = await DemoApp.StatusAsync(client, slot, Clara);
        Assert.Equal("fixed", DemoApp.Activity(clara, "Kletterwald Linz").GetProperty("status").GetString());
        Assert.NotEqual("fixed", DemoApp.Activity(clara, "Tiergarten Walding").GetProperty("status").GetString());
        Assert.Contains(("movedUp", Clara), await DemoApp.LogAsync(organiser, slot));
        Assert.Contains(await DemoApp.MailSubjectsAsync(organiser, slot), s => s.StartsWith("Nachgerückt"));
    }

    [Fact]
    public async Task David_cancelling_in_the_closed_vote_tells_the_organisers_and_nobody_moves_up()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var client = app.CreateClient();
        var seat = (await DemoApp.StatusAsync(client, slot, David)).GetProperty("activities").EnumerateArray()
            .First(a => a.GetProperty("status").GetString() == "fixed");

        (await DemoApp.GiveBackAsync(client, slot, David, seat.GetProperty("activityId").GetString()!, replacement: true)).EnsureSuccessStatusCode();

        var log = await DemoApp.LogAsync(organiser, slot);
        Assert.Contains(("cancelled", David), log);
        Assert.DoesNotContain(log, l => l.Kind == "movedUp");
        Assert.Contains(await DemoApp.MailSubjectsAsync(organiser, slot), s => s.StartsWith("Absage"));
    }

    [Fact]
    public async Task Changes_to_the_participant_list_after_the_draft_are_logged_with_the_account()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");

        (await organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/participants", new { name = "Eva Probe" })).EnsureSuccessStatusCode();

        var entries = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation/log");
        var listed = entries.EnumerateArray().Single(l => l.GetProperty("kind").GetString() == "listed");
        Assert.Equal("Eva Probe", listed.GetProperty("person").GetString());
        Assert.False(string.IsNullOrEmpty(listed.GetProperty("actor").GetString()));
    }

    [Fact]
    public async Task Once_seen_an_allocation_goes_internal_but_not_back_to_registration_or_recalculated()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
        async Task<HttpResponseMessage> PhaseAsync(string phase)
        {
            var body = System.Text.Json.Nodes.JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
            body["phase"] = phase;
            body["registrationDeadline"] = DateTime.Now.AddDays(7).ToString("s");
            return await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await PhaseAsync("Allocation")).StatusCode);
        var reopen = await PhaseAsync("Open");
        var recalculate = await organiser.PostAsync($"/api/admin/slots/{slot}/allocation/recalculate", null);

        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);
        Assert.Contains("wasVisible", await reopen.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, recalculate.StatusCode);
        Assert.Contains("wasVisible", await recalculate.Content.ReadAsStringAsync());
        Assert.True((await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation")).GetProperty("wasVisible").GetBoolean());
    }

    [Fact]
    public async Task An_allocation_nobody_has_seen_can_go_back_to_registration()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Firm");
        async Task<HttpStatusCode> PhaseAsync(string phase)
        {
            var body = System.Text.Json.Nodes.JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
            body["phase"] = phase;
            body["registrationDeadline"] = DateTime.Now.AddDays(7).ToString("s");
            return (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.NoContent, await PhaseAsync("Allocation"));
        Assert.Equal(HttpStatusCode.OK, (await organiser.PostAsync($"/api/admin/slots/{slot}/allocation/recalculate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await PhaseAsync("Open"));
    }

    [Fact]
    public async Task Info_names_the_version_and_browser_errors_are_accepted()
    {
        using var app = new DemoApp();
        var client = app.CreateClient();

        var info = await client.GetFromJsonAsync<JsonElement>("/api/info");
        Assert.False(string.IsNullOrEmpty(info.GetProperty("version").GetString()));
        var response = await client.PostAsJsonAsync("/api/client-error", new { message = "TypeError: x is undefined", path = "/p/secret-token" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
