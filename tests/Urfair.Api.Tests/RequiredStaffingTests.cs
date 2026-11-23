using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Urfair.Api.Tests;

/// <summary>Step 7 in the demo "Pfarrfest": required staffing (people still missing per date) and "more than required".</summary>
public class RequiredStaffingTests
{
	const string David = "David Doppler";

	static IEnumerable<JsonElement> Sessions(JsonElement activities) =>
		activities.EnumerateArray().SelectMany(a => a.GetProperty("sessions").EnumerateArray());

	static bool HasMissing(JsonElement session) =>
		session.TryGetProperty("missing", out var m) && m.ValueKind != JsonValueKind.Null;

	[Fact]
	public async Task Participants_see_how_many_people_are_still_missing_per_date()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var client = app.CreateClient();
		var festival = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");

		var sessions = Sessions(await client.GetFromJsonAsync<JsonElement>($"/api/slots/{festival}/activities")).ToList();
		Assert.All(sessions.Where(s => s.GetProperty("capacity").ValueKind == JsonValueKind.Number), s => Assert.True(HasMissing(s)));
		Assert.Contains(sessions, s => HasMissing(s) && s.GetProperty("missing").GetInt32() < s.GetProperty("capacity").GetInt32());
		Assert.Contains(sessions, s => HasMissing(s) && s.GetProperty("missing").GetInt32() > 0);

		var holiday = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
		Assert.DoesNotContain(Sessions(await client.GetFromJsonAsync<JsonElement>($"/api/slots/{holiday}/activities")), HasMissing);
	}

	[Fact]
	public async Task David_offers_one_seat_more_and_gets_a_third_shift()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var festival = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");

		var allocation = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{festival}/allocation");
		var seats = allocation.GetProperty("participants").EnumerateArray().Select(p =>
			(Name: p.GetProperty("name").GetString(), Count: p.GetProperty("assignments").GetArrayLength())).ToList();
		Assert.Equal(3, seats.Single(p => p.Name == David).Count);
		Assert.All(seats.Where(p => p.Name != David), p => Assert.Equal(2, p.Count));
	}

	[Fact]
	public async Task The_extra_offer_is_stored_reloaded_and_limited_to_the_maximum()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var client = app.CreateClient();
		var festival = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");

		var lookup = await (await client.PostAsJsonAsync($"/api/slots/{festival}/ballots/lookup", new { name = David })).Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(1, lookup.GetProperty("extra").GetInt32());
		var points = lookup.GetProperty("points").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

		var tooMany = await client.PostAsJsonAsync($"/api/slots/{festival}/ballots", new { name = David, points, extra = 3 });
		Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
		Assert.Contains("extraInvalid", await tooMany.Content.ReadAsStringAsync());

		(await client.PostAsJsonAsync($"/api/slots/{festival}/ballots", new { name = David, points, extra = 2 })).EnsureSuccessStatusCode();
		lookup = await (await client.PostAsJsonAsync($"/api/slots/{festival}/ballots/lookup", new { name = David })).Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(2, lookup.GetProperty("extra").GetInt32());
	}

	[Fact]
	public async Task Without_more_than_required_an_extra_offer_is_rejected()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var client = app.CreateClient();
		var festival = await DemoApp.SlotIdAsync(organiser, "Pfarrfest");
		var body = System.Text.Json.Nodes.JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{festival}"))!;
		body["rules"]!["maxExtra"] = 0;
		Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{festival}", body)).StatusCode);

		// David's earlier offer is cut down to the new maximum.
		var lookup = await (await client.PostAsJsonAsync($"/api/slots/{festival}/ballots/lookup", new { name = David })).Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(0, lookup.GetProperty("extra").GetInt32());
		var points = lookup.GetProperty("points").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
		var response = await client.PostAsJsonAsync($"/api/slots/{festival}/ballots", new { name = David, points, extra = 1 });
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}
}
