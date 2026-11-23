using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Urfair.Api.Tests;

/// <summary>"Open to everyone" with an exclusion list, in the demo "Pfarrfest" (open to everyone).</summary>
public class OpenWithExclusionsTests
{
	const string David = "David Doppler";
	const string Clara = "Clara Czerny";

	static async Task<HttpResponseMessage> SaveAsync(HttpClient organiser, Guid slot, Action<JsonObject> change)
	{
		var settings = (await organiser.GetFromJsonAsync<JsonNode>($"/api/admin/slots/{slot}"))!.AsObject();
		change(settings);
		return await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", settings);
	}

	static async Task<List<string>> ExcludedAsync(HttpClient organiser, Guid slot) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}")).GetProperty("excluded").EnumerateArray().Select(x => x.GetString()!).ToList();

	static async Task<bool> SeesAsync(HttpClient client, Guid slot, string person)
	{
		var mine = await (await client.PostAsJsonAsync("/api/my-slots", new { name = person })).Content.ReadFromJsonAsync<JsonElement>();
		return mine.EnumerateArray().Any(s => s.GetProperty("id").GetGuid() == slot);
	}

	[Fact]
	public async Task An_excluded_person_no_longer_sees_or_votes_unless_they_are_on_the_participant_list()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var client = app.CreateClient();
		var festival = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");
		Assert.True(await SeesAsync(client, festival, David));

		var saved = await SaveAsync(organiser, festival, s => s["excluded"] = new JsonArray(" david doppler ", "", "DAVID DOPPLER"));
		Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
		Assert.Equal(["david doppler"], await ExcludedAsync(organiser, festival));
		Assert.Contains(("settings", (string?)null), await DemoApp.LogAsync(organiser, festival));

		Assert.False(await SeesAsync(client, festival, David));
		Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/slots/{festival}/my-status", new { name = David })).StatusCode);
		var ballot = await client.PostAsJsonAsync($"/api/slots/{festival}/ballots", new { name = David, points = new Dictionary<string, int>() });
		Assert.Contains("notListed", await ballot.Content.ReadAsStringAsync());
		Assert.True(await SeesAsync(client, festival, Clara));
		await DemoApp.StatusAsync(client, festival, Clara);

		(await organiser.PostAsJsonAsync($"/api/admin/slots/{festival}/participants", new { name = David })).EnsureSuccessStatusCode();
		Assert.True(await SeesAsync(client, festival, David));
		await DemoApp.StatusAsync(client, festival, David);
	}

	[Fact]
	public async Task The_list_is_kept_without_a_new_one_checked_and_copied_with_the_vote()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var festival = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");
		(await SaveAsync(organiser, festival, s => s["excluded"] = new JsonArray(David))).EnsureSuccessStatusCode();

		(await SaveAsync(organiser, festival, s => s.Remove("excluded"))).EnsureSuccessStatusCode();
		Assert.Equal([David], await ExcludedAsync(organiser, festival));

		var tooLong = await SaveAsync(organiser, festival, s => s["excluded"] = new JsonArray(new string('x', 121)));
		Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
		Assert.Contains("textTooLong", await tooLong.Content.ReadAsStringAsync());
		Assert.Equal([David], await ExcludedAsync(organiser, festival));

		var copy = await organiser.PostAsJsonAsync("/api/admin/slots", new { name = "Pfarrfest 2028", withSampleData = false, copyFrom = festival, shiftWeeks = 52 });
		var id = (await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
		Assert.Equal([David], await ExcludedAsync(organiser, id));
	}
}
