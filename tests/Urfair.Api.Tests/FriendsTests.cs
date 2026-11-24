using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Urfair.Core;

namespace Urfair.Api.Tests;

/// <summary>"Together with friends" in the demo "Firmvorbereitung" (groups up to 3; B and C share a code).</summary>
public class FriendsTests
{
    const string Ben = "Ben Berger";
    const string Clara = "Clara Czerny";
    const string David = "David Doppler";

    static async Task<JsonElement> LookupAsync(HttpClient client, Guid slot, string person) =>
        await (await client.PostAsJsonAsync($"/api/slots/{slot}/ballots/lookup", new { name = person })).Content.ReadFromJsonAsync<JsonElement>();

    static Task<HttpResponseMessage> VoteAsync(HttpClient client, Guid slot, string person, JsonElement lookup, string? friends) =>
        client.PostAsJsonAsync($"/api/slots/{slot}/ballots", new
        {
            name = person,
            points = lookup.GetProperty("points").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32()),
            unavailableSessions = lookup.GetProperty("unavailableSessions").EnumerateArray().Select(x => x.GetString()).ToList(),
            friends,
        });

    [Fact]
    public async Task Friends_see_each_other_and_share_an_activity_in_the_preview()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var client = app.CreateClient();
        var firmung = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");

        var ben = await LookupAsync(client, firmung, Ben);
        Assert.Equal(DemoData.FriendCode, ben.GetProperty("friends").GetString());
        Assert.Equal([Clara], ben.GetProperty("friendNames").EnumerateArray().Select(x => x.GetString()));

        var allocation = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{firmung}/allocation");
        HashSet<string> Sessions(string name) => allocation.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == name).GetProperty("assignments").EnumerateArray()
            .Select(a => a.GetProperty("sessionId").GetString()!).ToHashSet();
        Assert.True(Sessions(Ben).Overlaps(Sessions(Clara)));
    }

    [Fact]
    public async Task A_full_group_takes_nobody_else_and_switching_off_dissolves_the_groups()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var client = app.CreateClient();
        var firmung = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");
        var david = await LookupAsync(client, firmung, David);

        // Group size 2: B and C already fill it.
        var settings = (await organiser.GetFromJsonAsync<JsonNode>($"/api/admin/slots/{firmung}"))!.AsObject();
        settings["rules"]!["friends"] = 2;
        Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{firmung}", settings)).StatusCode);
        var full = await VoteAsync(client, firmung, David, david, DemoData.FriendCode.ToLowerInvariant());
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        Assert.Contains("friendsFull", await full.Content.ReadAsStringAsync());

        // Up to 3: D joins with the code in any case.
        settings["rules"]!["friends"] = 3;
        (await organiser.PutAsJsonAsync($"/api/admin/slots/{firmung}", settings)).EnsureSuccessStatusCode();
        (await VoteAsync(client, firmung, David, david, " firm42 ")).EnsureSuccessStatusCode();
        Assert.Equal([Ben, Clara], (await LookupAsync(client, firmung, David)).GetProperty("friendNames").EnumerateArray().Select(x => x.GetString()));

        // Back to pairs: the group of three is too large and dissolved; switched off, codes are refused.
        settings["rules"]!["friends"] = 2;
        (await organiser.PutAsJsonAsync($"/api/admin/slots/{firmung}", settings)).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await LookupAsync(client, firmung, Ben)).GetProperty("friends").ValueKind);
        settings["rules"]!["friends"] = 0;
        (await organiser.PutAsJsonAsync($"/api/admin/slots/{firmung}", settings)).EnsureSuccessStatusCode();
        var off = await VoteAsync(client, firmung, David, david, "ABCD");
        Assert.Contains("friendsOff", await off.Content.ReadAsStringAsync());
    }
}
