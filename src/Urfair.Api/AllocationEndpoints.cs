using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record AssignmentDto(string ActivityId, string Title, string? GroupId, string SessionId, DateTime Start, DateTime? End, bool Flexible, string Location, int Points,
	bool IsFallback, bool Locked);
public record ParticipantDto(string BallotId, string Name, bool IsSample, double Satisfaction, IReadOnlyList<AssignmentDto> Assignments);
public record AllocationDto(int Seed, int PerParticipant, Metrics PointBased, Metrics FirstComeFirstServed,
	IReadOnlyDictionary<string, int> SessionLoad, IReadOnlyList<ParticipantDto> Participants, DateTime? FrozenAt);
public record SeatRequest(string SessionId, bool Locked);
public record SeatsRequest(List<SeatRequest>? Seats);

/// <summary>
/// The allocation of a FairSlot. Until it is frozen it is calculated on every request (live, e.g. for the interim status).
/// Freezing stores it; afterwards organisers move people by hand, fix seats and recalculate the rest, and participants
/// see exactly the stored result. Publishing freezes automatically.
/// </summary>
public static class AllocationEndpoints
{
	public static void MapAllocationAdmin(this RouteGroupBuilder admin)
	{
		admin.MapGet("/slots/{id:guid}/allocation", async (Guid id, AppDbContext db) =>
			await db.FairSlots.FindAsync(id) is { } slot ? Results.Ok(await AllocateAsync(db, slot)) : Results.NotFound());

		// Only after registration has closed, so no ballot can be missing or change afterwards.
		admin.MapPost("/slots/{id:guid}/allocation/freeze", async (Guid id, AppDbContext db) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null) return Results.NotFound();
			if (slot.Phase is Phase.Draft or Phase.Open) return Endpoints.Errors("freezeNeedsClosed");
			if (slot.FrozenAt is not null) return Endpoints.Errors("allocationFrozen");
			await FreezeAsync(db, slot);
			return Results.Ok(await AllocateAsync(db, slot));
		});

		// Fixed seats stay, everything else is allocated again.
		admin.MapPost("/slots/{id:guid}/allocation/recalculate", async (Guid id, AppDbContext db) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null) return Results.NotFound();
			if (slot.FrozenAt is null) return Endpoints.Errors("notFrozen");
			var locked = await db.Assignments.Where(a => a.FairSlotId == id && a.Locked).ToListAsync();
			await FreezeAsync(db, slot, locked.Select(a => a.ToModel()).ToList());
			return Results.Ok(await AllocateAsync(db, slot));
		});

		// Back to the live calculation; moves by hand are lost. Not once the result is published.
		admin.MapDelete("/slots/{id:guid}/allocation", async (Guid id, AppDbContext db) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null) return Results.NotFound();
			if (slot.Phase == Phase.Published) return Endpoints.Errors("unpublishFirst");
			await UnfreezeAsync(db, slot);
			return Results.Ok(await AllocateAsync(db, slot));
		});

		// All seats of one participant, set by hand: move, add, remove, fix. Full sessions may be overbooked on purpose.
		admin.MapPut("/slots/{id:guid}/allocation/{ballotId:guid}", async (Guid id, Guid ballotId, SeatsRequest req, AppDbContext db) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null || !await db.Ballots.AnyAsync(b => b.Id == ballotId && b.FairSlotId == id)) return Results.NotFound();
			if (slot.FrozenAt is null) return Endpoints.Errors("notFrozen");
			var activityOf = (await Database.LoadActivitiesAsync(db, id)).SelectMany(a => a.Sessions).ToDictionary(s => s.Id, s => s.ActivityId);
			var seats = req.Seats ?? [];
			if (seats.Any(s => !activityOf.ContainsKey(s.SessionId ?? ""))) return Endpoints.Errors("unknownSession");
			if (seats.Select(s => activityOf[s.SessionId]).Distinct().Count() < seats.Count) return Endpoints.Errors("sameActivityTwice");

			db.Assignments.RemoveRange(await db.Assignments.Where(a => a.BallotId == ballotId).ToListAsync());
			db.Assignments.AddRange(seats.Select(s => new AssignmentEntity { FairSlotId = id, BallotId = ballotId, SessionId = s.SessionId, Locked = s.Locked }));
			await db.SaveChangesAsync();
			return Results.Ok(await AllocateAsync(db, slot));
		});
	}

	/// <summary>The valid allocation: the stored one once frozen, otherwise calculated now.</summary>
	public static async Task<(Allocator Allocation, List<Activity> Activities, List<BallotEntity> Ballots)> CurrentAsync(AppDbContext db, FairSlotEntity slot)
	{
		var activities = await Database.LoadActivitiesAsync(db, slot.Id);
		var entities = await Database.LoadBallotsAsync(db, slot.Id);
		var ballots = entities.Select(b => b.ToModel()).ToList();
		var groups = slot.ToRules().Groups;
		var allocation = slot.FrozenAt is null
			? Allocator.PointBased(activities, ballots, slot.PerParticipant, slot.Seed, groups)
			: Allocator.FromAssignments(activities, ballots,
				(await db.Assignments.AsNoTracking().Where(a => a.FairSlotId == slot.Id).ToListAsync()).Select(a => a.ToModel()), groups);
		return (allocation, activities, entities);
	}

	/// <summary>Calculates the allocation (keeping <paramref name="locked"/> seats) and stores it. The slot must be tracked.</summary>
	public static async Task FreezeAsync(AppDbContext db, FairSlotEntity slot, IReadOnlyList<FixedAssignment>? locked = null)
	{
		var activities = await Database.LoadActivitiesAsync(db, slot.Id);
		var ballots = (await Database.LoadBallotsAsync(db, slot.Id)).Select(b => b.ToModel()).ToList();
		var result = Allocator.PointBased(activities, ballots, slot.PerParticipant, slot.Seed, slot.ToRules().Groups, locked);
		db.Assignments.RemoveRange(await db.Assignments.Where(a => a.FairSlotId == slot.Id).ToListAsync());
		db.Assignments.AddRange(result.Assignments.SelectMany(p => p.Value.Select(x =>
			new AssignmentEntity { FairSlotId = slot.Id, BallotId = Guid.Parse(p.Key), SessionId = x.Session.Id, Locked = x.Locked })));
		slot.FrozenAt = DateTime.UtcNow;
		await db.SaveChangesAsync();
	}

	public static async Task UnfreezeAsync(AppDbContext db, FairSlotEntity slot)
	{
		db.Assignments.RemoveRange(await db.Assignments.Where(a => a.FairSlotId == slot.Id).ToListAsync());
		slot.FrozenAt = null;
		await db.SaveChangesAsync();
	}

	/// <summary>The valid allocation shaped for the UI, compared with "first come, first served" on the same ballots.</summary>
	public static async Task<AllocationDto> AllocateAsync(AppDbContext db, FairSlotEntity slot)
	{
		var (allocation, activities, entities) = await CurrentAsync(db, slot);
		var ballots = entities.Select(b => b.ToModel()).ToList();
		var k = slot.PerParticipant;
		var fcfs = Allocator.FirstComeFirstServed(activities, ballots, k, slot.Seed, slot.ToRules().Groups);
		var titles = activities.ToDictionary(a => a.Id, a => a.Title);
		var groupOf = activities.ToDictionary(a => a.Id, a => a.GroupId);
		var isSample = entities.ToDictionary(b => b.Id.ToString(), b => b.IsSample);

		var participants = ballots.Select(b => new ParticipantDto(b.Id, b.Name, isSample[b.Id], allocation.Satisfaction(b, k),
			allocation.Assignments[b.Id].OrderBy(x => x.Session.Start).Select(x => new AssignmentDto(
				x.Session.ActivityId, titles[x.Session.ActivityId], groupOf[x.Session.ActivityId], x.Session.Id, x.Session.Start, x.Session.End,
				x.Session.Flexible, x.Session.Location, x.Points, x.IsFallback, x.Locked)).ToList())).ToList();

		var load = activities.SelectMany(a => a.Sessions).ToDictionary(x => x.Id, allocation.Load);
		// Stored as UTC; the database returns it without a kind.
		DateTime? frozen = slot.FrozenAt is { } f ? DateTime.SpecifyKind(f, DateTimeKind.Utc) : null;
		return new AllocationDto(slot.Seed, k, allocation.Evaluate(k), fcfs.Evaluate(k), load, participants, frozen);
	}
}
