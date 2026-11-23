using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Urfair.Api.Tests;

/// <summary>Choosing which seat to give up when moving up, in the demo "Ferienprogramm" made visible again.</summary>
public class GiveUpChoiceTests
{
	const string Clara = "Clara Czerny", David = "David Doppler", Kochen = "Kochen mit Oma";

	static async Task<List<string>> SeatsAsync(HttpClient organiser, Guid slot, string person) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation")).GetProperty("participants").EnumerateArray()
			.Single(p => p.GetProperty("name").GetString() == person).GetProperty("assignments").EnumerateArray()
			.Select(a => a.GetProperty("title").GetString()!).ToList();

	static string Id(JsonElement status, string title) => DemoApp.Activity(status, title).GetProperty("activityId").GetString()!;

	static async Task SetPhaseAsync(HttpClient organiser, Guid slot, string phase)
	{
		var body = System.Text.Json.Nodes.JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
		body["phase"] = phase;
		Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body)).StatusCode);
	}

	[Fact]
	public async Task Clara_chooses_to_give_up_Parkbad_instead_of_her_weakest_seat_and_takes_the_offer_with_it()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
		await SetPhaseAsync(organiser, slot, "Published");
		var client = app.CreateClient();

		var status = await DemoApp.StatusAsync(client, slot, Clara);
		var kochen = DemoApp.Activity(status, Kochen);
		Assert.True(kochen.GetProperty("waitlisted").GetBoolean());
		Assert.Equal("Fußballturnier", kochen.GetProperty("givesUp").GetString()); // pre-selected: the weakest
		Assert.Equal(["Fußballturnier", "Parkbad-Nachmittag"],
			kochen.GetProperty("giveUpOptions").EnumerateArray().Select(o => o.GetProperty("title").GetString()));
		var (kochenId, parkbad) = (Id(status, Kochen), Id(status, "Parkbad-Nachmittag"));

		// A choice for an activity she did not tick is dropped.
		var set = await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new
		{
			name = Clara, activityIds = new[] { kochenId },
			giveUp = new Dictionary<string, string> { [kochenId] = parkbad, ["unknown"] = parkbad },
		});
		set.EnsureSuccessStatusCode();
		kochen = DemoApp.Activity(await set.Content.ReadFromJsonAsync<JsonElement>(), Kochen);
		Assert.Equal("Parkbad-Nachmittag", kochen.GetProperty("givesUp").GetString());
		Assert.Equal(parkbad, kochen.GetProperty("giveUpChoice").GetString());

		// Ticking without a choice keeps the earlier one.
		(await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = new[] { kochenId } })).EnsureSuccessStatusCode();
		Assert.Equal(parkbad, DemoApp.Activity(await DemoApp.StatusAsync(client, slot, Clara), Kochen).GetProperty("giveUpChoice").GetString());

		// Lists final: David cancels and his seat is offered at once; Clara's offer names the seat she chose.
		await SetPhaseAsync(organiser, slot, "Closed");
		(await DemoApp.GiveBackAsync(client, slot, David, kochenId, replacement: false)).EnsureSuccessStatusCode();

		var offer = (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer");
		Assert.Equal("Parkbad-Nachmittag", offer.GetProperty("givesUp").GetString());
		var offerId = offer.GetProperty("id").GetString();
		(await client.PostAsJsonAsync($"/api/slots/{slot}/offers/{offerId}/accept", new { name = Clara })).EnsureSuccessStatusCode();

		Assert.Equal([Kochen, "Fußballturnier"], await SeatsAsync(organiser, slot, Clara));
	}

	[Fact]
	public async Task A_choice_is_undone_with_the_tick()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
		await SetPhaseAsync(organiser, slot, "Published");
		var client = app.CreateClient();
		var status = await DemoApp.StatusAsync(client, slot, Clara);
		var (kochenId, parkbad) = (Id(status, Kochen), Id(status, "Parkbad-Nachmittag"));
		(await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new
		{
			name = Clara, activityIds = new[] { kochenId }, giveUp = new Dictionary<string, string> { [kochenId] = parkbad },
		})).EnsureSuccessStatusCode();

		(await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = Array.Empty<string>() })).EnsureSuccessStatusCode();
		(await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = new[] { kochenId } })).EnsureSuccessStatusCode();

		var kochen = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, Clara), Kochen);
		Assert.Equal(JsonValueKind.Null, kochen.GetProperty("giveUpChoice").ValueKind);
		Assert.Equal("Fußballturnier", kochen.GetProperty("givesUp").GetString());
	}
}
