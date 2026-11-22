using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record AssignmentDto(string ActivityId, string Title, string? GroupId, string SessionId, DateTime Start, DateTime? End, bool Flexible, string Location, int Points,
	bool IsFallback, bool Locked);
public record ParticipantDto(string BallotId, string Name, bool IsSample, double Satisfaction, IReadOnlyList<AssignmentDto> Assignments,
	IReadOnlyDictionary<string, int> Points, IReadOnlyList<string> Original);
public record AllocationDto(int Seed, int PerParticipant, Metrics PointBased, Metrics FirstComeFirstServed,
	IReadOnlyDictionary<string, int> SessionLoad, IReadOnlyList<ParticipantDto> Participants, DateTime? FrozenAt);
public record SeatRequest(string SessionId, bool Locked);
public record SeatsRequest(List<SeatRequest>? Seats);
public record LogDto(DateTime At, string Actor, string Kind, string? Person, string? Before, string? After);
public record ListPersonDto(string Name, string? Parish, string? Email, DateOnly? BirthDate, DateOnly? BaptismDate);
public record ListSessionDto(string Id, DateTime Start, DateTime? End, bool Flexible, string Location, int? Capacity, IReadOnlyList<ListPersonDto> People);
public record ListActivityDto(string Id, string Title, string? Leader, string? LeaderEmail, string? GroupId, IReadOnlyList<ListSessionDto> Sessions);
public record LeaderListsDto(bool Frozen, IReadOnlyList<ListActivityDto> Activities);
public record LeaderMailRequest(List<string>? ActivityIds, bool WithEmail, bool WithBirthDate, bool WithBaptismDate = false);

/// <summary>
/// The allocation of a FairSlot. Until it is frozen it is calculated on every request (live, e.g. for the interim status).
/// Freezing stores it; afterwards organisers move people by hand, fix seats and recalculate the rest, and participants
/// see exactly the stored result. Publishing freezes automatically. Freezing also keeps the original result, and every
/// change is logged, so organisers can see what a person had before and go back to it.
/// </summary>
public static class AllocationEndpoints
{
	public static void MapAllocationAdmin(this RouteGroupBuilder admin)
	{
		admin.MapGet("/slots/{id:guid}/allocation", async (Guid id, AppDbContext db) =>
			await db.FairSlots.FindAsync(id) is { } slot ? Results.Ok(await AllocateAsync(db, slot)) : Results.NotFound());

		// Only after registration has closed, so no ballot can be missing or change afterwards.
		admin.MapPost("/slots/{id:guid}/allocation/freeze", async (Guid id, AppDbContext db, HttpContext http) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null) return Results.NotFound();
			if (slot.Phase is Phase.Draft or Phase.Open) return Endpoints.Errors("freezeNeedsClosed");
			if (slot.FrozenAt is not null) return Endpoints.Errors("allocationFrozen");
			Log(db, http, id, "frozen");
			await FreezeAsync(db, slot);
			return Results.Ok(await AllocateAsync(db, slot));
		});

		// Fixed seats stay, everything else is allocated again. The original from freezing is kept.
		admin.MapPost("/slots/{id:guid}/allocation/recalculate", async (Guid id, AppDbContext db, HttpContext http) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null) return Results.NotFound();
			if (slot.FrozenAt is null) return Endpoints.Errors("notFrozen");
			var locked = await db.Assignments.Where(a => a.FairSlotId == id && a.Locked).ToListAsync();
			Log(db, http, id, "recalculated");
			await FreezeAsync(db, slot, locked.Select(a => a.ToModel()).ToList());
			return Results.Ok(await AllocateAsync(db, slot));
		});

		// Back to the live calculation; moves by hand and the original are lost. Not once the result is published.
		admin.MapDelete("/slots/{id:guid}/allocation", async (Guid id, AppDbContext db, HttpContext http) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null) return Results.NotFound();
			if (slot.Phase == Phase.Published) return Endpoints.Errors("unpublishFirst");
			Log(db, http, id, "unfrozen");
			await UnfreezeAsync(db, slot);
			return Results.Ok(await AllocateAsync(db, slot));
		});

		// All seats of one participant, set by hand: move, add, remove, fix. Full sessions may be overbooked on purpose.
		admin.MapPut("/slots/{id:guid}/allocation/{ballotId:guid}", async (Guid id, Guid ballotId, SeatsRequest req, AppDbContext db, HttpContext http) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			var ballot = await db.Ballots.FirstOrDefaultAsync(b => b.Id == ballotId && b.FairSlotId == id);
			if (slot is null || ballot is null) return Results.NotFound();
			if (slot.FrozenAt is null) return Endpoints.Errors("notFrozen");
			var activities = await Database.LoadActivitiesAsync(db, id);
			var activityOf = activities.SelectMany(a => a.Sessions).ToDictionary(s => s.Id, s => s.ActivityId);
			var seats = req.Seats ?? [];
			if (seats.Any(s => !activityOf.ContainsKey(s.SessionId ?? ""))) return Endpoints.Errors("unknownSession");
			if (seats.Select(s => activityOf[s.SessionId]).Distinct().Count() < seats.Count) return Endpoints.Errors("sameActivityTwice");

			var old = await db.Assignments.Where(a => a.BallotId == ballotId).ToListAsync();
			var before = SeatText(activities, old.Select(a => a.SessionId));
			var after = SeatText(activities, seats.Select(s => s.SessionId));
			db.Assignments.RemoveRange(old);
			db.Assignments.AddRange(seats.Select(s => new AssignmentEntity { FairSlotId = id, BallotId = ballotId, SessionId = s.SessionId, Locked = s.Locked }));
			if (before != after) Log(db, http, id, "seats", ballot.Name, before, after);
			await db.SaveChangesAsync();
			return Results.Ok(await AllocateAsync(db, slot));
		});

		// Changes to the frozen allocation, newest first.
		admin.MapGet("/slots/{id:guid}/allocation/log", async (Guid id, AppDbContext db) =>
			(await db.AllocationLog.AsNoTracking().Where(l => l.FairSlotId == id).ToListAsync())
				.OrderByDescending(l => l.At).Take(300)
				.Select(l => new LogDto(DateTime.SpecifyKind(l.At, DateTimeKind.Utc), l.Actor, l.Kind, l.Person, l.Before, l.After)));

		// Who goes where, per activity and date, for the activity leaders.
		admin.MapGet("/slots/{id:guid}/allocation/lists", async (Guid id, AppDbContext db, HttpContext http) =>
			await db.FairSlots.FindAsync(id) is { } slot ? Results.Ok(await ListsAsync(db, slot, Access.Slot(http))) : Results.NotFound());

		// One mail per leader e-mail address with the lists of their activities, shown with the invitations. Only once frozen,
		// and only for accounts that see all participants, as the mails are not limited to a Pfarrgemeinde.
		admin.MapPost("/slots/{id:guid}/allocation/leader-mails", async (Guid id, LeaderMailRequest req, AppDbContext db, HttpContext http) =>
		{
			var slot = await db.FairSlots.FindAsync(id);
			if (slot is null) return Results.NotFound();
			if (slot.FrozenAt is null) return Endpoints.Errors("notFrozen");
			var access = Access.Slot(http);
			if (access.Scope is not null) return Endpoints.Errors("leaderMailsNeedAll");
			var wanted = req.ActivityIds is { Count: > 0 } ids ? ids.ToHashSet() : null;
			var chosen = (await ListsAsync(db, slot, access)).Activities.Where(a => wanted is null || wanted.Contains(a.Id)).ToList();
			var mails = chosen.Where(a => a.LeaderEmail is not null)
				.GroupBy(a => a.LeaderEmail!.ToLowerInvariant())
				.Select(g => LeaderMail(slot, g.ToList(), req)).ToList();
			db.Mails.AddRange(mails);
			await db.SaveChangesAsync();
			return Results.Ok(new { created = mails.Count, withoutEmail = chosen.Count(a => a.LeaderEmail is null) });
		});
	}

	/// <summary>
	/// The lists per activity and date. E-mail address and dates of birth and baptism only of participants in the account's own
	/// Pfarrgemeinden (as in the participant list). Without a leader the vote's organiser is named, as on the activity.
	/// </summary>
	static async Task<LeaderListsDto> ListsAsync(AppDbContext db, FairSlotEntity slot, SlotAccess access)
	{
		var (allocation, activities, ballots) = await CurrentAsync(db, slot);
		var listed = (await db.Participants.AsNoTracking().Where(p => p.FairSlotId == slot.Id).ToListAsync())
			.GroupBy(p => p.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
		var person = ballots.ToDictionary(b => b.Id.ToString(), b =>
			listed.GetValueOrDefault(b.Name.ToLowerInvariant()) is { } p
				? access.InScope(p.Parish) ? new ListPersonDto(b.Name, p.Parish, p.Email, p.BirthDate, p.BaptismDate) : new ListPersonDto(b.Name, p.Parish, null, null, null)
				: new ListPersonDto(b.Name, null, null, null, null));
		var bySession = allocation.Assignments
			.SelectMany(kv => kv.Value.Select(x => (x.Session.Id, Person: person[kv.Key])))
			.GroupBy(x => x.Id)
			.ToDictionary(g => g.Key, g => (IReadOnlyList<ListPersonDto>)g.Select(x => x.Person).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
		return new LeaderListsDto(slot.FrozenAt is not null, activities.Select(a => new ListActivityDto(a.Id, a.Title, a.Leader ?? slot.Organiser, a.LeaderEmail, a.GroupId,
			a.Sessions.OrderBy(s => s.Start).Select(s => new ListSessionDto(s.Id, s.Start, s.End, s.Flexible, s.Location, s.Capacity,
				bySession.GetValueOrDefault(s.Id, []))).ToList())).ToList());
	}

	/// <summary>The lists of one leader's activities as a plain-text mail.</summary>
	static MailEntity LeaderMail(FairSlotEntity slot, List<ListActivityDto> activities, LeaderMailRequest req)
	{
		var leader = activities[0].Leader;
		var body = new StringBuilder()
			.Append(leader is null ? "Hallo,\n\n" : $"Hallo {leader},\n\n")
			.Append($"hier sind die Teilnahmelisten für deine Veranstaltungen bei „{slot.Name}“.\n");
		foreach (var a in activities)
		foreach (var s in a.Sessions)
		{
			body.Append($"\n{a.Title}\n{When(s)}");
			if (s.Location.Length > 0) body.Append($" · {s.Location}");
			body.Append(s.Capacity is { } c ? $" · {s.People.Count} von {c} Plätzen\n" : $" · {s.People.Count} Personen\n");
			if (s.People.Count == 0) body.Append("Niemand zugeteilt.\n");
			foreach (var (p, i) in s.People.Select((p, i) => (p, i)))
			{
				var parts = new List<string> { p.Name };
				if (p.Parish is not null) parts.Add(p.Parish);
				if (req.WithBirthDate && p.BirthDate is { } birth) parts.Add($"geb. {birth:dd.MM.yyyy}");
				if (req.WithBaptismDate && p.BaptismDate is { } baptism) parts.Add($"getauft {baptism:dd.MM.yyyy}");
				if (req.WithEmail && p.Email is not null) parts.Add(p.Email);
				body.Append($"{i + 1,3}. {string.Join(", ", parts)}\n");
			}
		}
		body.Append("\nÄndert sich noch etwas, bekommst du eine neue Liste.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
		return new MailEntity { FairSlotId = slot.Id, To = activities[0].LeaderEmail!, Subject = $"Teilnahmeliste: {slot.Name}", Body = body.ToString() };
	}

	static string When(ListSessionDto s) =>
		s.Flexible ? $"bis {s.Start:dd.MM.yyyy}, nach Vereinbarung"
		: s.End is not { } end ? $"{s.Start:dd.MM.yyyy, HH:mm} Uhr"
		: end.Date == s.Start.Date ? $"{s.Start:dd.MM.yyyy, HH:mm}–{end:HH:mm} Uhr"
		: $"{s.Start:dd.MM.yyyy, HH:mm} Uhr bis {end:dd.MM.yyyy, HH:mm} Uhr";

	/// <summary>Adds a log entry; it is saved with the next SaveChanges.</summary>
	public static void Log(AppDbContext db, HttpContext http, Guid slotId, string kind, string? person = null, string? before = null, string? after = null) =>
		db.AllocationLog.Add(new AllocationLogEntity { FairSlotId = slotId, Actor = Access.Account(http).Name, Kind = kind, Person = person, Before = before, After = after });

	/// <summary>Seats as readable text for the log: activity titles in date order, with the date where an activity has several.</summary>
	static string SeatText(List<Activity> activities, IEnumerable<string> sessionIds)
	{
		var ids = sessionIds.ToHashSet();
		return string.Join(", ", activities
			.SelectMany(a => a.Sessions.Where(s => ids.Contains(s.Id)).Select(s => (a, s)))
			.OrderBy(x => x.s.Start)
			.Select(x => x.a.Sessions.Count > 1 && !x.s.Flexible ? $"{x.a.Title} ({x.s.Start:dd.MM. HH:mm})" : x.a.Title));
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

	/// <summary>
	/// Calculates the allocation (keeping <paramref name="locked"/> seats) and stores it. A fresh freeze (no locked seats
	/// given) also stores the result as the original; recalculating keeps the original. The slot must be tracked.
	/// </summary>
	public static async Task FreezeAsync(AppDbContext db, FairSlotEntity slot, IReadOnlyList<FixedAssignment>? locked = null)
	{
		var activities = await Database.LoadActivitiesAsync(db, slot.Id);
		var ballots = (await Database.LoadBallotsAsync(db, slot.Id)).Select(b => b.ToModel()).ToList();
		var result = Allocator.PointBased(activities, ballots, slot.PerParticipant, slot.Seed, slot.ToRules().Groups, locked);
		var seats = result.Assignments.SelectMany(p => p.Value.Select(x => (Ballot: Guid.Parse(p.Key), x.Session.Id, x.Locked))).ToList();
		db.Assignments.RemoveRange(await db.Assignments.Where(a => a.FairSlotId == slot.Id).ToListAsync());
		db.Assignments.AddRange(seats.Select(s => new AssignmentEntity { FairSlotId = slot.Id, BallotId = s.Ballot, SessionId = s.Id, Locked = s.Locked }));
		if (locked is null)
		{
			db.OriginalAssignments.RemoveRange(await db.OriginalAssignments.Where(a => a.FairSlotId == slot.Id).ToListAsync());
			db.OriginalAssignments.AddRange(seats.Select(s => new OriginalAssignmentEntity { FairSlotId = slot.Id, BallotId = s.Ballot, SessionId = s.Id }));
		}
		slot.FrozenAt = DateTime.UtcNow;
		await db.SaveChangesAsync();
	}

	public static async Task UnfreezeAsync(AppDbContext db, FairSlotEntity slot)
	{
		db.Assignments.RemoveRange(await db.Assignments.Where(a => a.FairSlotId == slot.Id).ToListAsync());
		db.OriginalAssignments.RemoveRange(await db.OriginalAssignments.Where(a => a.FairSlotId == slot.Id).ToListAsync());
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
		var original = slot.FrozenAt is null ? [] : (await db.OriginalAssignments.AsNoTracking().Where(a => a.FairSlotId == slot.Id).ToListAsync())
			.GroupBy(a => a.BallotId.ToString()).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(a => a.SessionId).ToList());

		var participants = ballots.Select(b => new ParticipantDto(b.Id, b.Name, isSample[b.Id], allocation.Satisfaction(b, k),
			allocation.Assignments[b.Id].OrderBy(x => x.Session.Start).Select(x => new AssignmentDto(
				x.Session.ActivityId, titles[x.Session.ActivityId], groupOf[x.Session.ActivityId], x.Session.Id, x.Session.Start, x.Session.End,
				x.Session.Flexible, x.Session.Location, x.Points, x.IsFallback, x.Locked)).ToList(),
			b.Points, original.GetValueOrDefault(b.Id, []))).ToList();

		var load = activities.SelectMany(a => a.Sessions).ToDictionary(x => x.Id, allocation.Load);
		// Stored as UTC; the database returns it without a kind.
		DateTime? frozen = slot.FrozenAt is { } f ? DateTime.SpecifyKind(f, DateTimeKind.Utc) : null;
		return new AllocationDto(slot.Seed, k, allocation.Evaluate(k), fcfs.Evaluate(k), load, participants, frozen);
	}
}
