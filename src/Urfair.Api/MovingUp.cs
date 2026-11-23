using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

/// <summary>
/// While the allocation is visible (Published), free seats are filled on their own: after a seat is given back or removed,
/// capacity is added, a waiting-list tick is set, and once a minute. Each step is logged with Urfair as the actor.
/// Internal allocation (recalculate by hand) and Closed (organisers decide per seat) do not move anyone.
/// </summary>
public static class MovingUp
{
	public static async Task<int> FillAsync(AppDbContext db, FairSlotEntity slot)
	{
		if (slot.Phase != Phase.Published || slot.FrozenAt is null) return 0;
		var activities = await Database.LoadActivitiesAsync(db, slot.Id);
		var ballots = await db.Ballots.Include(b => b.Points).Where(b => b.FairSlotId == slot.Id).ToListAsync();
		var stored = await db.Assignments.Where(a => a.FairSlotId == slot.Id).ToListAsync();
		var allocation = Allocator.FromAssignments(activities, ballots.Select(b => b.ToModel()).ToList(), stored.Select(a => a.ToModel()), slot.ToRules().Groups);
		var moves = allocation.FillFreeSeats(slot.PerParticipant, slot.Seed);
		if (moves.Count == 0) return 0;

		var byId = ballots.ToDictionary(b => b.Id.ToString());
		var seatsBefore = new Dictionary<Guid, HashSet<string>>();
		foreach (var m in moves)
		{
			var ballot = byId[m.BallotId];
			var seats = stored.Where(a => a.BallotId == ballot.Id).ToList();
			seatsBefore.TryAdd(ballot.Id, seats.Select(a => a.SessionId).ToHashSet());
			var before = AllocationEndpoints.SeatText(activities, seats.Select(a => a.SessionId));
			if (m.GaveUp is { } old && seats.FirstOrDefault(a => a.SessionId == old.Id) is { } gone)
			{
				stored.Remove(gone);
				db.Assignments.Remove(gone);
			}
			var seat = new AssignmentEntity { FairSlotId = slot.Id, BallotId = ballot.Id, SessionId = m.Session.Id };
			stored.Add(seat);
			db.Assignments.Add(seat);
			ballot.Waitlist = ballot.Waitlist.Where(x => x != m.Session.ActivityId).ToList();
			ballot.KeepGiveUpForTicks();
			var after = AllocationEndpoints.SeatText(activities, stored.Where(a => a.BallotId == ballot.Id).Select(a => a.SessionId));
			AllocationEndpoints.Log(db, slot.Id, Phases.Clock, "movedUp", ballot.Name, before, after);
		}
		await NotifyAsync(db, slot, activities, stored, ballots.Where(b => seatsBefore.ContainsKey(b.Id)).ToList(), seatsBefore);
		await db.SaveChangesAsync();
		return moves.Count;
	}

	/// <summary>Everyone who moved up and is on the participant list with an e-mail address gets their new seats.</summary>
	static async Task NotifyAsync(AppDbContext db, FairSlotEntity slot, List<Activity> activities, List<AssignmentEntity> stored,
		List<BallotEntity> moved, Dictionary<Guid, HashSet<string>> seatsBefore)
	{
		var listed = (await db.Participants.Where(p => p.FairSlotId == slot.Id && p.Email != null).ToListAsync())
			.GroupBy(p => p.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
		foreach (var ballot in moved)
		{
			if (!listed.TryGetValue(ballot.Name.ToLowerInvariant(), out var p)) continue;
			var before = seatsBefore[ballot.Id];
			var after = stored.Where(a => a.BallotId == ballot.Id).Select(a => a.SessionId).ToHashSet();
			var gained = AllocationEndpoints.SeatText(activities, after.Except(before));
			if (gained.Length == 0) continue; // moved up and back within one chain
			var lost = AllocationEndpoints.SeatText(activities, before.Except(after));
			var body = new StringBuilder()
				.Append($"Hallo {p.Name},\n\n")
				.Append($"bei „{slot.Name}“ ist ein Platz frei geworden und du bist nachgerückt: {gained}.\n");
			if (lost.Length > 0) body.Append($"Dafür hast du deinen Platz bei {lost} abgegeben, wie bei deiner Vormerkung angegeben.\n");
			body.Append($"\nDeine Plätze jetzt: {AllocationEndpoints.SeatText(activities, after)}\n\n");
			body.Append(Mailing.KnownBaseUrl is { } url
				? $"Alle Termine siehst du unter deinem persönlichen Link:\n{url}/p/{p.Token}\n\nBitte gib den Link nicht weiter.\n\n"
				: "Alle Termine siehst du unter deinem persönlichen Link aus der Einladung.\n\n");
			body.Append(slot.Organiser ?? "Dein Organisationsteam");
			Mailing.Add(db, slot, new MailEntity { ParticipantId = p.Id, To = p.Email!, Subject = $"Nachgerückt: {slot.Name}", Body = body.ToString() });
		}
	}
}
