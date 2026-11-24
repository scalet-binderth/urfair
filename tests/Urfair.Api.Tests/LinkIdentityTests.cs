using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Urfair.Api.Tests;

/// <summary>A personal link belongs to its vote (decided 24 November 2026): elsewhere it only counts in votes open to all.</summary>
public class LinkIdentityTests
{
    const string Ben = "Ben Berger";

    static async Task<string> TokenAsync(HttpClient organiser, Guid slot, string name) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/participants")).EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == name).GetProperty("token").GetString()!;

    static HttpClient WithLink(DemoApp app, string token)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Participant", token);
        client.DefaultRequestHeaders.Add("X-Participant-Check", "2012-07-02");
        client.DefaultRequestHeaders.Add("X-Participant-Baptism", "2012-09-16");
        return client;
    }

    [Fact]
    public async Task A_link_of_one_vote_does_not_act_in_another_vote_that_lists_the_name()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var firmung = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");
        var holiday = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var festival = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");
        var firmungLink = WithLink(app, await TokenAsync(organiser, firmung, Ben));

        var mine = (await (await firmungLink.PostAsJsonAsync("/api/my-slots", new { })).Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(firmung, mine);
        Assert.Contains(festival, mine); // open to all, Ben is not listed there
        Assert.DoesNotContain(holiday, mine);

        Assert.Equal(HttpStatusCode.OK, (await firmungLink.PostAsJsonAsync($"/api/slots/{firmung}/my-status", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await firmungLink.PostAsJsonAsync($"/api/slots/{festival}/my-status", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await firmungLink.PostAsJsonAsync($"/api/slots/{holiday}/my-status", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await firmungLink.PostAsJsonAsync($"/api/slots/{holiday}/ballots/lookup", new { })).StatusCode);

        var holidayLink = WithLink(app, await TokenAsync(organiser, holiday, Ben));
        Assert.Equal(HttpStatusCode.OK, (await holidayLink.PostAsJsonAsync($"/api/slots/{holiday}/my-status", new { })).StatusCode);
    }
}
