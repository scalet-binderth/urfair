using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Urfair.Api.Tests;

/// <summary>Undo with save points in the demo "Ministrantenausflug" (SavePoints.cs).</summary>
public class SavePointTests
{
	const string Ben = "Ben Berger", Clara = "Clara Czerny", David = "David Doppler";

	static async Task<List<JsonElement>> LogAsync(HttpClient organiser, Guid slot) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation/log")).EnumerateArray().ToList();

	static async Task<List<string>> SeatsAsync(HttpClient organiser, Guid slot, string person) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation")).GetProperty("participants").EnumerateArray()
			.Single(p => p.GetProperty("name").GetString() == person).GetProperty("assignments").EnumerateArray()
			.Select(a => a.GetProperty("title").GetString()!).ToList();

	static string Id(JsonElement entry) => entry.GetProperty("id").GetString()!;

	static async Task SetPhaseAsync(HttpClient organiser, Guid slot, string phase)
	{
		var body = System.Text.Json.Nodes.JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
		body["phase"] = phase;
		Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body)).StatusCode);
	}

	[Fact]
	public async Task A_moving_up_chain_is_undone_while_internal_and_the_undo_can_be_undone()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
		var client = app.CreateClient();
		var kletterwald = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, Clara), "Kletterwald Linz").GetProperty("activityId").GetString()!;
		(await client.PostAsJsonAsync($"/api/slots/{slot}/waitlist", new { name = Clara, activityIds = new[] { kletterwald } })).EnsureSuccessStatusCode();
		(await DemoApp.GiveBackAsync(client, slot, David, kletterwald, replacement: false)).EnsureSuccessStatusCode();
		Assert.Contains("Kletterwald Linz", await SeatsAsync(organiser, slot, Clara));
		await SetPhaseAsync(organiser, slot, "Allocation");

		var log = await LogAsync(organiser, slot);
		var frozen = log.Single(l => l.GetProperty("kind").GetString() == "frozen");
		Assert.True(frozen.GetProperty("canRestore").GetBoolean());
		var preview = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation/restore/{Id(frozen)}");
		var changes = preview.GetProperty("changes").EnumerateArray().ToList();
		Assert.Contains(changes, c => c.GetProperty("name").GetString() == Clara && c.GetProperty("before").GetString()!.Contains("Kletterwald"));
		Assert.Contains(changes, c => c.GetProperty("name").GetString() == David && c.GetProperty("keptGivenBack").GetString() == "Kletterwald Linz");
		Assert.Equal(0, preview.GetProperty("mails").GetInt32());
		Assert.False(preview.GetProperty("movesUp").GetBoolean());

		(await organiser.PostAsync($"/api/admin/slots/{slot}/allocation/restore/{Id(frozen)}", null)).EnsureSuccessStatusCode();

		Assert.DoesNotContain("Kletterwald Linz", await SeatsAsync(organiser, slot, Clara));
		Assert.Contains("Tiergarten Walding", await SeatsAsync(organiser, slot, Clara));
		Assert.DoesNotContain("Kletterwald Linz", await SeatsAsync(organiser, slot, David));
		log = await LogAsync(organiser, slot);
		Assert.Equal("restored", log[0].GetProperty("kind").GetString());
		Assert.False(log[0].GetProperty("canRestore").GetBoolean()); // the current state
		Assert.Contains(log, l => l.GetProperty("kind").GetString() == "seats" && l.GetProperty("person").GetString() == Clara);

		// A chain stores one save point, with its last entry: the newest one before the restore is the state after moving up.
		var beforeRestore = log.First(l => l.GetProperty("canRestore").GetBoolean());
		Assert.Equal("movedUp", beforeRestore.GetProperty("kind").GetString());
		(await organiser.PostAsync($"/api/admin/slots/{slot}/allocation/restore/{Id(beforeRestore)}", null)).EnsureSuccessStatusCode();
		Assert.Contains("Kletterwald Linz", await SeatsAsync(organiser, slot, Clara));
	}

	[Fact]
	public async Task Undoing_a_change_by_hand_while_visible_mails_the_person()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
		var allocation = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation");
		string BallotOf(string name) => allocation.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name).GetProperty("ballotId").GetString()!;
		var tiergarten = allocation.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("name").GetString() == Clara)
			.GetProperty("assignments").EnumerateArray().Single(a => a.GetProperty("title").GetString() == "Tiergarten Walding").GetProperty("sessionId").GetString();
		var frozen = Id((await LogAsync(organiser, slot)).Single(l => l.GetProperty("kind").GetString() == "frozen"));

		(await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}/allocation/{BallotOf(Ben)}",
			new { seats = new[] { new { sessionId = tiergarten, locked = true } } })).EnsureSuccessStatusCode();
		Assert.Equal(["Tiergarten Walding"], await SeatsAsync(organiser, slot, Ben));
		var preview = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation/restore/{frozen}");
		Assert.True(preview.GetProperty("movesUp").GetBoolean());
		Assert.True(preview.GetProperty("mails").GetInt32() >= 1);
		var mailsBefore = (await DemoApp.MailSubjectsAsync(organiser, slot)).Count(s => s.StartsWith("Deine Plätze"));

		(await organiser.PostAsync($"/api/admin/slots/{slot}/allocation/restore/{frozen}", null)).EnsureSuccessStatusCode();

		Assert.Equal(["Ars Electronica Center"], await SeatsAsync(organiser, slot, Ben));
		Assert.True((await DemoApp.MailSubjectsAsync(organiser, slot)).Count(s => s.StartsWith("Deine Plätze")) > mailsBefore);
	}

	[Fact]
	public async Task Nothing_can_be_restored_before_the_allocation_is_frozen()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Firm");

		Assert.Empty(await LogAsync(organiser, slot));
		Assert.Equal(HttpStatusCode.NotFound, (await organiser.PostAsync($"/api/admin/slots/{slot}/allocation/restore/{Guid.NewGuid()}", null)).StatusCode);
	}
}
