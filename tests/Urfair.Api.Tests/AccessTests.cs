using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Urfair.Api.Tests;

/// <summary>Permissions of a Pfarrgemeinde admin in a "Junia, read only" vote (decided 24 November 2026).</summary>
public class AccessTests
{
    const string Markus = "Pfarrgemeinde Urfahr-St. Markus", Geist = "Pfarrgemeinde Urfahr-Hl. Geist";

    static async Task<List<JsonElement>> ParticipantsAsync(HttpClient organiser, Guid slot) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/participants")).EnumerateArray().ToList();

    [Fact]
    public async Task A_parish_admin_maintains_only_their_own_participants_in_a_read_only_vote_and_cannot_change_it()
    {
        using var app = new DemoApp();
        var junia = await app.OrganiserAsync();
        var firmung = await DemoApp.SlotIdAsync(junia, "Firmvorbereitung");
        var markus = app.CreateClient();
        (await markus.PostAsJsonAsync("/api/auth/demo", new { key = "markus" })).EnsureSuccessStatusCode();

        Assert.Equal(["Ben Berger"], (await ParticipantsAsync(markus, firmung)).Select(p => p.GetProperty("name").GetString()));

        Assert.Equal(HttpStatusCode.NoContent,
            (await markus.PostAsJsonAsync($"/api/admin/slots/{firmung}/participants", new { name = "Emma Eder", parish = Markus })).StatusCode);
        var foreign = await markus.PostAsJsonAsync($"/api/admin/slots/{firmung}/participants", new { name = "Fritz Fink", parish = Geist });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Contains("parishNotOwn", await foreign.Content.ReadAsStringAsync());

        var ben = (await ParticipantsAsync(markus, firmung)).Single(p => p.GetProperty("name").GetString() == "Ben Berger").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent,
            (await markus.PutAsJsonAsync($"/api/admin/participants/{ben}", new { name = "Ben Berger", email = "ben.neu@example.org", parish = Markus })).StatusCode);
        var moved = await markus.PutAsJsonAsync($"/api/admin/participants/{ben}", new { name = "Ben Berger", parish = Geist });
        Assert.Contains("parishNotOwn", await moved.Content.ReadAsStringAsync());

        var clara = (await ParticipantsAsync(junia, firmung)).Single(p => p.GetProperty("name").GetString() == "Clara Czerny").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await markus.PutAsJsonAsync($"/api/admin/participants/{clara}", new { name = "Clara Czerny", parish = Geist })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await markus.DeleteAsync($"/api/admin/participants/{clara}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await markus.DeleteAsync($"/api/admin/slots/{firmung}")).StatusCode);
        Assert.Equal(["Ben Berger", "Emma Eder"], (await ParticipantsAsync(markus, firmung)).Select(p => p.GetProperty("name").GetString()).Order());
    }
}
