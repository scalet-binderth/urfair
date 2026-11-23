using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Urfair.Api.Tests;

/// <summary>Taking over the participants of another vote (POST /participants/copy).</summary>
public class ParticipantCopyTests
{
	static async Task<JsonElement> CopyAsync(HttpClient organiser, Guid slot, Guid from, bool commit)
	{
		var response = await organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/participants/copy", new { from, commit });
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		return await response.Content.ReadFromJsonAsync<JsonElement>();
	}

	static async Task<List<JsonElement>> ParticipantsAsync(HttpClient organiser, Guid slot) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/participants")).EnumerateArray().ToList();

	[Fact]
	public async Task People_are_taken_over_with_their_details_and_new_links_and_known_names_are_skipped()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var firmung = await DemoApp.SlotIdAsync(organiser, "Firmvorbereitung");
		var holiday = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
		var camp = await DemoApp.SlotIdAsync(organiser, "Sommerlager");
		Assert.Empty(await ParticipantsAsync(organiser, camp));

		var preview = await CopyAsync(organiser, camp, holiday, false);
		Assert.Equal((2, 0, false), (preview.GetProperty("new").GetInt32(), preview.GetProperty("known").GetInt32(), preview.GetProperty("saved").GetBoolean()));
		Assert.Empty(await ParticipantsAsync(organiser, camp));

		Assert.True((await CopyAsync(organiser, camp, holiday, true)).GetProperty("saved").GetBoolean());
		var source = (await ParticipantsAsync(organiser, holiday)).OrderBy(p => p.GetProperty("name").GetString()).ToList();
		var copied = (await ParticipantsAsync(organiser, camp)).OrderBy(p => p.GetProperty("name").GetString()).ToList();
		string Details(JsonElement p) => string.Join("|", new[] { "name", "email", "birthDate", "baptismDate", "parish" }.Select(k => p.GetProperty(k).ToString()));
		Assert.Equal(source.Select(Details), copied.Select(Details));
		Assert.Empty(source.Select(p => p.GetProperty("token").GetString()).Intersect(copied.Select(p => p.GetProperty("token").GetString())));
		Assert.All(copied, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("invitedAt").ValueKind));

		var again = await CopyAsync(organiser, camp, holiday, true);
		Assert.Equal((0, 2, false), (again.GetProperty("new").GetInt32(), again.GetProperty("known").GetInt32(), again.GetProperty("saved").GetBoolean()));

		// Into a vote after the draft: only the new person is added, and it is logged.
		var more = await CopyAsync(organiser, holiday, firmung, true);
		Assert.Equal((1, 2), (more.GetProperty("new").GetInt32(), more.GetProperty("known").GetInt32()));
		Assert.Equal(3, (await ParticipantsAsync(organiser, holiday)).Count);
		Assert.Contains(("participantsCopied", (string?)null), await DemoApp.LogAsync(organiser, holiday));
	}

	[Fact]
	public async Task A_parish_admin_takes_over_only_people_of_their_own_parish_from_votes_they_see()
	{
		using var app = new DemoApp();
		var junia = await app.OrganiserAsync();
		var firmung = await DemoApp.SlotIdAsync(junia, "Firmvorbereitung");
		var excursion = await DemoApp.SlotIdAsync(junia, "Ministrantenausflug");
		var festival = await DemoApp.SlotIdAsync(junia, "Pfarrfest");
		var markus = app.CreateClient();
		(await markus.PostAsJsonAsync("/api/auth/demo", new { key = "markus" })).EnsureSuccessStatusCode();

		var hidden = await markus.PostAsJsonAsync($"/api/admin/slots/{festival}/participants/copy", new { from = excursion, commit = true });
		Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

		Assert.True((await CopyAsync(markus, festival, firmung, true)).GetProperty("saved").GetBoolean());
		Assert.Equal(["Ben Berger"], (await ParticipantsAsync(junia, festival)).Select(p => p.GetProperty("name").GetString()));
	}
}
