using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Urfair.Core;

namespace Urfair.Api.Tests;

/// <summary>Undoing changes to activities, the participant list and the rules from the log (Undo.cs).</summary>
public class UndoTests
{
	const string Ben = "Ben Berger", Clara = "Clara Czerny";

	static async Task<List<JsonElement>> LogAsync(HttpClient organiser, Guid slot) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation/log")).EnumerateArray().ToList();

	static async Task<JsonElement> LatestAsync(HttpClient organiser, Guid slot, string kind) =>
		(await LogAsync(organiser, slot)).First(l => l.GetProperty("kind").GetString() == kind);

	static Task<HttpResponseMessage> UndoAsync(HttpClient organiser, Guid slot, JsonElement entry) =>
		organiser.PostAsync($"/api/admin/slots/{slot}/log/{entry.GetProperty("id").GetString()}/undo", null);

	static async Task<List<string>> SeatsAsync(HttpClient organiser, Guid slot, string person) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation")).GetProperty("participants").EnumerateArray()
			.Single(p => p.GetProperty("name").GetString() == person).GetProperty("assignments").EnumerateArray()
			.Select(a => a.GetProperty("title").GetString()!).ToList();

	static async Task<List<JsonElement>> ActivitiesAsync(HttpClient organiser, Guid slot) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/activities")).EnumerateArray().ToList();

	static async Task<List<JsonElement>> ParticipantsAsync(HttpClient organiser, Guid slot) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/participants")).EnumerateArray().ToList();

	[Fact]
	public async Task A_deleted_activity_comes_back_with_its_dates_points_and_seats()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Ministrantenausflug");
		var tiergarten = (await ActivitiesAsync(organiser, slot)).Single(a => a.GetProperty("title").GetString() == "Tiergarten Walding");
		var id = tiergarten.GetProperty("id").GetString()!;
		var points = (await DemoApp.StatusAsync(app.CreateClient(), slot, Clara)).GetProperty("activities").EnumerateArray()
			.Single(a => a.GetProperty("activityId").GetString() == id).GetProperty("points").GetInt32();
		Assert.Contains("Tiergarten Walding", await SeatsAsync(organiser, slot, Clara));

		(await organiser.DeleteAsync($"/api/admin/activities/{id}")).EnsureSuccessStatusCode();
		Assert.DoesNotContain("Tiergarten Walding", await SeatsAsync(organiser, slot, Clara));
		var removed = await LatestAsync(organiser, slot, "activityRemoved");
		Assert.True(removed.GetProperty("canUndo").GetBoolean());

		Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(organiser, slot, removed)).StatusCode);
		var back = (await ActivitiesAsync(organiser, slot)).Single(a => a.GetProperty("id").GetString() == id);
		Assert.Equal(tiergarten.GetProperty("sessions").ToString(), back.GetProperty("sessions").ToString());
		Assert.Contains("Tiergarten Walding", await SeatsAsync(organiser, slot, Clara));
		Assert.Equal(points, (await DemoApp.StatusAsync(app.CreateClient(), slot, Clara)).GetProperty("activities").EnumerateArray()
			.Single(a => a.GetProperty("activityId").GetString() == id).GetProperty("points").GetInt32());

		var log = await LogAsync(organiser, slot);
		Assert.False(log.Single(l => l.GetProperty("id").GetString() == removed.GetProperty("id").GetString()).GetProperty("canUndo").GetBoolean());
		Assert.Contains(log, l => l.GetProperty("kind").GetString() == "seats" && l.GetProperty("person").GetString() == Clara);
		var undone = log.First(l => l.GetProperty("kind").GetString() == "undone");
		Assert.Equal("activityRemoved", undone.GetProperty("before").GetString());

		// Undoing the undo would delete it again, but people sit in it now: an undo never takes a seat away.
		var again = await UndoAsync(organiser, slot, undone);
		Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
		Assert.Contains("undoSeats", await again.Content.ReadAsStringAsync());
		var twice = await UndoAsync(organiser, slot, removed);
		Assert.Contains("undoGone", await twice.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task A_changed_activity_gets_its_details_back_and_the_undo_can_be_undone()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");
		var activity = (await ActivitiesAsync(organiser, slot))[0];
		var id = activity.GetProperty("id").GetString()!;
		var title = activity.GetProperty("title").GetString()!;
		var body = JsonNode.Parse(activity.GetRawText())!.AsObject();
		body["title"] = "Geändert";
		Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/activities/{id}", body)).StatusCode);

		(await UndoAsync(organiser, slot, await LatestAsync(organiser, slot, "activity"))).EnsureSuccessStatusCode();
		Assert.Equal(title, (await ActivitiesAsync(organiser, slot)).Single(a => a.GetProperty("id").GetString() == id).GetProperty("title").GetString());
		(await UndoAsync(organiser, slot, await LatestAsync(organiser, slot, "undone"))).EnsureSuccessStatusCode();
		Assert.Equal("Geändert", (await ActivitiesAsync(organiser, slot)).Single(a => a.GetProperty("id").GetString() == id).GetProperty("title").GetString());
	}

	[Fact]
	public async Task A_removed_participant_comes_back_with_the_same_personal_link()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var slot = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");
		var ben = (await ParticipantsAsync(organiser, slot)).Single(p => p.GetProperty("name").GetString() == Ben);
		(await organiser.DeleteAsync($"/api/admin/participants/{ben.GetProperty("id").GetString()}")).EnsureSuccessStatusCode();
		Assert.DoesNotContain(await ParticipantsAsync(organiser, slot), p => p.GetProperty("name").GetString() == Ben);

		(await UndoAsync(organiser, slot, await LatestAsync(organiser, slot, "unlisted"))).EnsureSuccessStatusCode();
		var back = (await ParticipantsAsync(organiser, slot)).Single(p => p.GetProperty("name").GetString() == Ben);
		Assert.Equal(ben.GetRawText(), back.GetRawText());

		// Undoing the undo removes him again.
		(await UndoAsync(organiser, slot, await LatestAsync(organiser, slot, "undone"))).EnsureSuccessStatusCode();
		Assert.DoesNotContain(await ParticipantsAsync(organiser, slot), p => p.GetProperty("name").GetString() == Ben);
	}

	[Fact]
	public async Task Undoing_a_rule_change_brings_back_what_it_cut_down()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var client = app.CreateClient();
		var slot = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");
		async Task<string?> CodeAsync() =>
			(await (await client.PostAsJsonAsync($"/api/slots/{slot}/ballots/lookup", new { name = Ben })).Content.ReadFromJsonAsync<JsonElement>())
				.GetProperty("friends").GetString();
		var settings = (await organiser.GetFromJsonAsync<JsonNode>($"/api/admin/slots/{slot}"))!.AsObject();
		settings["rules"]!["friends"] = 0;
		(await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", settings)).EnsureSuccessStatusCode();
		Assert.Null(await CodeAsync());

		(await UndoAsync(organiser, slot, await LatestAsync(organiser, slot, "settings"))).EnsureSuccessStatusCode();
		Assert.Equal(3, (await organiser.GetFromJsonAsync<JsonNode>($"/api/admin/slots/{slot}"))!["rules"]!["friends"]!.GetValue<int>());
		Assert.Equal(DemoData.FriendCode, await CodeAsync());
	}

	[Fact]
	public async Task Changes_in_the_draft_are_not_logged_and_a_frozen_vote_keeps_its_rules()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var camp = await DemoApp.SlotIdAsync(organiser, "Sommerlager");
		var id = (await ActivitiesAsync(organiser, camp))[0].GetProperty("id").GetString();
		(await organiser.DeleteAsync($"/api/admin/activities/{id}")).EnsureSuccessStatusCode();
		Assert.DoesNotContain(await LogAsync(organiser, camp), l => l.GetProperty("canUndo").GetBoolean());

		// A rule change before freezing cannot be undone once the allocation is frozen (it would no longer be valid).
		var slot = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");
		var settings = (await organiser.GetFromJsonAsync<JsonNode>($"/api/admin/slots/{slot}"))!.AsObject();
		settings["rules"]!["friends"] = 2;
		(await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", settings)).EnsureSuccessStatusCode();
		var change = await LatestAsync(organiser, slot, "settings");
		settings["phase"] = "Allocation";
		(await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", settings)).EnsureSuccessStatusCode();
		var refused = await UndoAsync(organiser, slot, change);
		Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
		Assert.Contains("allocationFrozen", await refused.Content.ReadAsStringAsync());
	}
}
