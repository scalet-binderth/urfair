using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Urfair.Api.Tests;

/// <summary>Leaders of a vote and their addresses (Leaders.cs): one place to change them, required addresses, import.</summary>
public class LeadersTests
{
    const string Hermine = "hermine.gruber@example.org";

    static async Task<List<JsonElement>> LeadersAsync(HttpClient organiser, Guid slot) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/leaders")).EnumerateArray().ToList();

    static async Task<List<JsonElement>> ActivitiesAsync(HttpClient organiser, Guid slot) =>
        (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/activities")).EnumerateArray().ToList();

    static List<string> Titles(JsonElement leader) => leader.GetProperty("activities").EnumerateArray().Select(a => a.GetProperty("title").GetString()!).ToList();

    [Fact]
    public async Task The_organiser_comes_first_with_the_activities_they_lead_then_the_other_leaders()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");

        var leaders = await LeadersAsync(organiser, slot);

        Assert.Equal([("Miriam Koller", "miriam.koller@example.org", true), ("Florian Stadler", "florian.stadler@example.org", false)],
            leaders.Select(l => (l.GetProperty("name").GetString(), l.GetProperty("email").GetString(), l.GetProperty("isOrganiser").GetBoolean())));
        Assert.Equal(["Tiergarten Walding", "Kletterwald Linz"], Titles(leaders[0]));
    }

    [Fact]
    public async Task Changing_a_leaders_address_changes_it_on_all_their_activities()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");

        var response = await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}/leaders",
            new { oldName = "Hermine Gruber", isOrganiser = false, name = "Hermine Gruber-Hofer", email = "hermine@example.org" });

        response.EnsureSuccessStatusCode();
        var hers = (await ActivitiesAsync(organiser, slot)).Where(a => a.GetProperty("title").GetString() is "Kochen mit Oma" or "Kreativwerkstatt").ToList();
        Assert.All(hers, a => Assert.Equal(("Hermine Gruber-Hofer", "hermine@example.org"), (a.GetProperty("leader").GetString(), a.GetProperty("leaderEmail").GetString())));
        Assert.Contains(await DemoApp.LogAsync(organiser, slot), l => l == ("leader", "Hermine Gruber-Hofer"));
    }

    [Fact]
    public async Task The_organiser_row_changes_the_settings_too()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");

        (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}/leaders",
            new { oldName = "Miriam Koller", isOrganiser = true, name = "Pfarre Hl. Geist", email = "hlgeist@example.org" })).EnsureSuccessStatusCode();

        var settings = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}");
        Assert.Equal(("Pfarre Hl. Geist", "hlgeist@example.org"), (settings.GetProperty("organiser").GetString(), settings.GetProperty("organiserEmail").GetString()));
        var first = (await LeadersAsync(organiser, slot))[0];
        Assert.Equal(["Tiergarten Walding", "Kletterwald Linz"], Titles(first));
        Assert.Equal("hlgeist@example.org", first.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Addresses_are_required_and_shared_by_all_activities_of_a_leader()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
        var kochen = (await ActivitiesAsync(organiser, slot)).Single(a => a.GetProperty("title").GetString() == "Kochen mit Oma");
        var body = JsonNode.Parse(kochen.GetRawText())!.AsObject();

        body["leaderEmail"] = null;
        var missing = await organiser.PutAsJsonAsync($"/api/admin/activities/{kochen.GetProperty("id").GetString()}", body);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("leaderEmailRequired", await missing.Content.ReadAsStringAsync());

        body["leaderEmail"] = "oma@example.org";
        (await organiser.PutAsJsonAsync($"/api/admin/activities/{kochen.GetProperty("id").GetString()}", body)).EnsureSuccessStatusCode();
        var kreativ = (await ActivitiesAsync(organiser, slot)).Single(a => a.GetProperty("title").GetString() == "Kreativwerkstatt");
        Assert.Equal("oma@example.org", kreativ.GetProperty("leaderEmail").GetString());

        var settings = JsonNode.Parse((await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}")).GetRawText())!.AsObject();
        settings["organiserEmail"] = " ";
        var noOrganiser = await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", settings);
        Assert.Equal(HttpStatusCode.BadRequest, noOrganiser.StatusCode);
        Assert.Contains("organiserEmailRequired", await noOrganiser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Activities_without_a_leader_send_their_lists_to_the_organiser()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");

        var lists = (await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation/lists")).GetProperty("activities").EnumerateArray().ToList();

        var withoutLeader = lists.First(a => a.GetProperty("leader").GetString() == "Firmteam Urfahr");
        Assert.Equal("firmteam@example.org", withoutLeader.GetProperty("leaderEmail").GetString());
        Assert.Equal("theresa.ebner@example.org", lists.Single(a => a.GetProperty("leader").GetString() == "Theresa Ebner").GetProperty("leaderEmail").GetString());
    }

    [Fact]
    public async Task The_import_names_leaders_without_an_address_and_takes_the_ones_entered()
    {
        using var app = new DemoApp();
        var organiser = await app.OrganiserAsync();
        var slot = await DemoApp.SlotIdAsync(organiser, "Jungschar-Sommerlager");
        var csv = "Titel;Leitung;Leitung E-Mail;Beginn;Ende;Ort;Plätze\n" +
            "Basteln;Anna Neu;;01.07.2027 10:00;01.07.2027 12:00;Pfarrheim;10\n" +
            "Singen;Hermine Gruber;hermine@example.org;02.07.2027 10:00;02.07.2027 12:00;Pfarrheim;10\n" +
            "Spielen;;;03.07.2027 10:00;03.07.2027 12:00;Pfarrheim;10\n";

        var preview = await (await organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/import", new { csv, replace = false, commit = false }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var leaders = preview.GetProperty("leaders").EnumerateArray()
            .Select(l => (l.GetProperty("name").GetString(), l.GetProperty("email").GetString(), l.GetProperty("isOrganiser").GetBoolean())).ToList();
        Assert.Equal([("Jungschar Christkönig", "jungschar.christkoenig@example.org", true), ("Anna Neu", null, false), ("Hermine Gruber", "hermine@example.org", false)], leaders);

        var refused = await organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/import", new { csv, replace = false, commit = true });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("leaderEmailMissing", await refused.Content.ReadAsStringAsync());

        (await organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/import",
            new { csv, replace = false, commit = true, leaderEmails = new Dictionary<string, string> { ["anna neu"] = "anna@example.org" } })).EnsureSuccessStatusCode();
        var basteln = (await ActivitiesAsync(organiser, slot)).Single(a => a.GetProperty("title").GetString() == "Basteln");
        Assert.Equal(("Anna Neu", "anna@example.org"), (basteln.GetProperty("leader").GetString(), basteln.GetProperty("leaderEmail").GetString()));
    }
}
