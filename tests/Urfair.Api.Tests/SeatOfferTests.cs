using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urfair.Api.Data;

namespace Urfair.Api.Tests;

/// <summary>Filling a seat after the lists are final, in the demo "Ferienprogramm" (SeatOffers.cs).</summary>
public class SeatOfferTests
{
	const string Clara = "Clara Czerny", David = "David Doppler", Kochen = "Kochen mit Oma";

	static async Task<JsonElement> PersonAsync(HttpClient organiser, Guid slot, string name) =>
		(await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/allocation")).GetProperty("participants").EnumerateArray()
			.Single(p => p.GetProperty("name").GetString() == name);

	static List<string> Seats(JsonElement person) =>
		person.GetProperty("assignments").EnumerateArray().Select(a => a.GetProperty("title").GetString()!).ToList();

	/// <summary>David cancels "Kochen mit Oma"; returns the slot, a participant client and the session of his seat.</summary>
	static async Task<(Guid Slot, HttpClient Client, string Session)> DavidCancelsAsync(HttpClient organiser, DemoApp app)
	{
		var slot = await DemoApp.SlotIdAsync(organiser, "Ferienprogramm");
		var session = (await PersonAsync(organiser, slot, David)).GetProperty("assignments").EnumerateArray()
			.Single(a => a.GetProperty("title").GetString() == Kochen).GetProperty("sessionId").GetString()!;
		var client = app.CreateClient();
		var kochen = DemoApp.Activity(await DemoApp.StatusAsync(client, slot, David), Kochen).GetProperty("activityId").GetString()!;
		(await DemoApp.GiveBackAsync(client, slot, David, kochen, replacement: true)).EnsureSuccessStatusCode();
		return (slot, client, session);
	}

	static Task<HttpResponseMessage> FillAsync(HttpClient organiser, Guid slot, string session) =>
		organiser.PostAsJsonAsync($"/api/admin/slots/{slot}/offers", new { sessionId = session });

	static Task<HttpResponseMessage> AnswerAsync(HttpClient client, Guid slot, string offer, string answer) =>
		client.PostAsJsonAsync($"/api/slots/{slot}/offers/{offer}/{answer}", new { name = Clara });

	[Fact]
	public async Task The_seat_is_offered_to_Clara_who_ticked_it_and_after_she_accepts_David_gets_suggestions()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var (slot, client, session) = await DavidCancelsAsync(organiser, app);
		Assert.Equal(["Parkbad-Nachmittag", "Fußballturnier"], Seats(await PersonAsync(organiser, slot, Clara)));

		Assert.Equal(HttpStatusCode.NoContent, (await FillAsync(organiser, slot, session)).StatusCode);

		var offer = (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer");
		Assert.Equal(Kochen, offer.GetProperty("title").GetString());
		Assert.Equal("Fußballturnier", offer.GetProperty("givesUp").GetString());
		Assert.Contains(await DemoApp.MailSubjectsAsync(organiser, slot), s => s.StartsWith("Freier Platz für dich"));
		Assert.Contains("noFreeSeat", await (await FillAsync(organiser, slot, session)).Content.ReadAsStringAsync()); // held for Clara

		var accepted = await AnswerAsync(client, slot, offer.GetProperty("id").GetString()!, "accept");

		accepted.EnsureSuccessStatusCode();
		Assert.Equal(JsonValueKind.Null, (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("offer").ValueKind);
		Assert.Equal([Kochen, "Parkbad-Nachmittag"], Seats(await PersonAsync(organiser, slot, Clara)));
		var log = await DemoApp.LogAsync(organiser, slot);
		Assert.Contains(("offered", Clara), log);
		Assert.Contains(("offerAccepted", Clara), log);
		Assert.Equal("offerNone", log[0].Kind); // nobody else wants Fußballturnier
		var david = await PersonAsync(organiser, slot, David);
		Assert.True(david.GetProperty("needsReplacement").GetBoolean());
		var suggestions = david.GetProperty("suggestions").EnumerateArray()
			.Select(s => (s.GetProperty("title").GetString(), s.GetProperty("kind").GetString())).ToList();
		Assert.Contains(("Fußballturnier", "free"), suggestions);
		Assert.Contains(("Kreativwerkstatt", "overbook"), suggestions);
	}

	[Fact]
	public async Task A_declined_offer_goes_to_the_next_person_and_seats_stay()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var (slot, client, session) = await DavidCancelsAsync(organiser, app);
		(await FillAsync(organiser, slot, session)).EnsureSuccessStatusCode();
		var offer = (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer").GetProperty("id").GetString()!;

		(await AnswerAsync(client, slot, offer, "decline")).EnsureSuccessStatusCode();

		Assert.Equal(["Parkbad-Nachmittag", "Fußballturnier"], Seats(await PersonAsync(organiser, slot, Clara)));
		var log = await DemoApp.LogAsync(organiser, slot);
		Assert.Contains(("offerDeclined", Clara), log);
		Assert.Equal("offerNone", log[0].Kind);
		Assert.Equal(HttpStatusCode.BadRequest, (await AnswerAsync(client, slot, offer, "accept")).StatusCode);
		Assert.Contains("nobodyLeft", await (await FillAsync(organiser, slot, session)).Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task An_expired_offer_is_mailed_and_leaving_the_phase_withdraws_open_offers()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var (slot, client, session) = await DavidCancelsAsync(organiser, app);
		(await FillAsync(organiser, slot, session)).EnsureSuccessStatusCode();
		using (var scope = app.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			(await db.SeatOffers.SingleAsync()).ExpiresAt = Phases.Now().AddMinutes(-1);
			await db.SaveChangesAsync();
			await Phases.TickAsync(db);
		}

		Assert.Contains(("offerExpired", Clara), await DemoApp.LogAsync(organiser, slot));
		Assert.Contains(await DemoApp.MailSubjectsAsync(organiser, slot), s => s.StartsWith("Angebot abgelaufen"));
		Assert.Equal(JsonValueKind.Null, (await DemoApp.StatusAsync(client, slot, Clara)).GetProperty("offer").ValueKind);

		// A new round: the organisers may ask again only people not yet asked, so nobody is left.
		Assert.Contains("nobodyLeft", await (await FillAsync(organiser, slot, session)).Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task Leaving_closed_withdraws_an_open_offer()
	{
		using var app = new DemoApp();
		var organiser = await app.OrganiserAsync();
		var (slot, client, session) = await DavidCancelsAsync(organiser, app);
		(await FillAsync(organiser, slot, session)).EnsureSuccessStatusCode();

		var body = System.Text.Json.Nodes.JsonNode.Parse(await organiser.GetStringAsync($"/api/admin/slots/{slot}"))!;
		body["phase"] = "Published";
		Assert.Equal(HttpStatusCode.NoContent, (await organiser.PutAsJsonAsync($"/api/admin/slots/{slot}", body)).StatusCode);

		Assert.Contains(("offerWithdrawn", Clara), await DemoApp.LogAsync(organiser, slot));
		var offers = await organiser.GetFromJsonAsync<JsonElement>($"/api/admin/slots/{slot}/offers");
		Assert.Equal("withdrawn", offers.EnumerateArray().Single().GetProperty("status").GetString());
	}
}
