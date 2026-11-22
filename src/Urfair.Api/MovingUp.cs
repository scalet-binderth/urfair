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
		foreach (var m in moves)
		{
			var ballot = byId[m.BallotId];
			var seats = stored.Where(a => a.BallotId == ballot.Id).ToList();
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
			var after = AllocationEndpoints.SeatText(activities, stored.Where(a => a.BallotId == ballot.Id).Select(a => a.SessionId));
			AllocationEndpoints.Log(db, slot.Id, Phases.Clock, "movedUp", ballot.Name, before, after);
		}
		await db.SaveChangesAsync();
		return moves.Count;
	}
}
