using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;

namespace Urfair.Api;

/// <param name="KeptGivenBack">Seats the person had then but has given back since; they stay given back.</param>
public record RestoreChangeDto(string Name, bool IsSample, string Before, string After, string? KeptGivenBack);
/// <param name="Mails">People who get „Deine Plätze“ (visible or closed, on the list with an e-mail address).</param>
/// <param name="MovesUp">Visible: seats that are free afterwards fill from the waiting list at once.</param>
public record RestorePreviewDto(DateTime At, IReadOnlyList<RestoreChangeDto> Changes, int Mails, bool MovesUp);

/// <summary>
/// Undo for the frozen allocation. After every change to seats (frozen, recalculated, by hand, moving up, given back,
/// cancelled, taken, restored) a save point with the new state is stored. Organisers can go back to the state after any of
/// these log entries: seats, fixed marks and waiting-list ticks are set back, seats a participant gave back themselves stay
/// given back. Restoring deletes nothing; it is logged (one entry per person and one for the whole) and is a save point
/// itself, so it can be undone too. Activities, the participant list and rules are not part of a save point.
/// </summary>
public static class SavePoints
{
    /// <summary>Log kinds after which a save point is stored.</summary>
    static readonly HashSet<string> Kinds = ["frozen", "recalculated", "seats", "movedUp", "givenBack", "cancelled", "tookSeat", "offerAccepted", "restored"];

    /// <summary>One ballot in a save point. Short names keep the JSON small.</summary>
    sealed record BallotState(
        [property: JsonPropertyName("b")] Guid Ballot,
        [property: JsonPropertyName("s")] List<string> Seats,
        [property: JsonPropertyName("l")] List<string> Locked,
        [property: JsonPropertyName("w")] List<string> Waitlist,
        [property: JsonPropertyName("d")] List<string> Declined,
        [property: JsonPropertyName("n")] List<string> NoReplacement,
        [property: JsonPropertyName("g")] List<string>? GiveUp = null);

    /// <summary>What restoring changes for one ballot.</summary>
    sealed record Change(BallotEntity Ballot, List<AssignmentEntity> Old, List<AssignmentEntity> New, List<string> Waitlist,
        List<string> NoReplacement, List<string> KeptGivenBack, List<string> GiveUp)
    {
        public bool SeatsChanged => !Old.Select(a => a.SessionId).ToHashSet().SetEquals(New.Select(a => a.SessionId));
        public bool Changed => SeatsChanged
            || !Old.Where(a => a.Locked).Select(a => a.SessionId).ToHashSet().SetEquals(New.Where(a => a.Locked).Select(a => a.SessionId))
            || !Waitlist.ToHashSet().SetEquals(Ballot.Waitlist) || !NoReplacement.ToHashSet().SetEquals(Ballot.NoReplacement)
            || !GiveUp.ToHashSet().SetEquals(Ballot.GiveUp);
    }

    /// <summary>Per FairSlot the log entry about to be saved that a save point belongs to (a restore wins, otherwise the newest).</summary>
    public static List<(Guid SlotId, Guid LogId)> Pending(AppDbContext db) =>
        db.ChangeTracker.Entries<AllocationLogEntity>()
            .Where(e => e.State == EntityState.Added && Kinds.Contains(e.Entity.Kind))
            .Select(e => e.Entity)
            .GroupBy(l => l.FairSlotId)
            .Select(g => (g.Key, g.OrderBy(l => l.Kind == "restored").ThenBy(l => l.At).Last().Id))
            .ToList();

    /// <summary>Stores the current state of a frozen allocation as the save point of a log entry (saved with the next SaveChanges).</summary>
    public static async Task AddAsync(AppDbContext db, Guid slotId, Guid logId, CancellationToken ct = default)
    {
        if (!await db.FairSlots.AnyAsync(s => s.Id == slotId && s.FrozenAt != null, ct)) return;
        var seats = (await db.Assignments.AsNoTracking().Where(a => a.FairSlotId == slotId).ToListAsync(ct)).ToLookup(a => a.BallotId);
        var ballots = await db.Ballots.AsNoTracking().Where(b => b.FairSlotId == slotId).ToListAsync(ct);
        var states = ballots.Select(b => new BallotState(b.Id,
            seats[b.Id].Select(a => a.SessionId).ToList(), seats[b.Id].Where(a => a.Locked).Select(a => a.SessionId).ToList(),
            b.Waitlist, b.Declined, b.NoReplacement, b.GiveUp)).ToList();
        db.AllocationSnapshots.Add(new AllocationSnapshotEntity { FairSlotId = slotId, LogId = logId, Data = JsonSerializer.Serialize(states) });
    }

    /// <summary>Log entries whose state can be restored: with a save point, since the last freeze, and not the current state.</summary>
    public static async Task<HashSet<Guid>> RestorableAsync(AppDbContext db, FairSlotEntity slot)
    {
        if (slot.FrozenAt is null || slot.Phase is not (Phase.Allocation or Phase.Published or Phase.Closed)) return [];
        var since = await LastFreezeAsync(db, slot.Id);
        var points = await (from s in db.AllocationSnapshots
                            join l in db.AllocationLog on s.LogId equals l.Id
                            where s.FairSlotId == slot.Id && l.At >= since
                            select new { l.Id, l.At }).ToListAsync();
        return points.OrderByDescending(p => p.At).Skip(1).Select(p => p.Id).ToHashSet();
    }

    static async Task<DateTime> LastFreezeAsync(AppDbContext db, Guid slotId) =>
        await db.AllocationLog.Where(l => l.FairSlotId == slotId && l.Kind == "frozen").Select(l => (DateTime?)l.At).MaxAsync() ?? DateTime.MinValue;

    public static void MapSavePoints(this RouteGroupBuilder admin)
    {
        // Who would gain or lose which seat when going back to the state after a log entry.
        admin.MapGet("/slots/{id:guid}/allocation/restore/{logId:guid}", async (Guid id, Guid logId, AppDbContext db, HttpContext http) =>
        {
            if (await db.FairSlots.FindAsync(id) is not { } slot) return Results.NotFound();
            var sees = await Access.SeesPersonAsync(http, db, id);
            var (error, entry, changes) = await PlanAsync(db, slot, logId);
            if (error is not null) return error;
            var activities = await Database.LoadActivitiesAsync(db, id);
            var shown = changes.Where(c => c.SeatsChanged || c.KeptGivenBack.Count > 0).OrderBy(c => c.Ballot.IsSample).ThenBy(c => c.Ballot.Name).ToList();
            var mails = 0;
            if (slot.Phase is Phase.Published or Phase.Closed)
            {
                var reachable = (await db.Participants.AsNoTracking().Where(p => p.FairSlotId == id && p.Email != null).Select(p => p.Name.ToLower()).ToListAsync()).ToHashSet();
                mails = changes.Count(c => c.SeatsChanged && !c.Ballot.IsSample && reachable.Contains(c.Ballot.Name.ToLower()));
            }
            return Results.Ok(new RestorePreviewDto(Database.Utc(entry!.At),
                shown.Select(c => new RestoreChangeDto(sees(c.Ballot.Name) ? c.Ballot.Name : Access.HiddenName, c.Ballot.IsSample,
                    AllocationEndpoints.SeatText(activities, c.Old.Select(a => a.SessionId)),
                    AllocationEndpoints.SeatText(activities, c.New.Select(a => a.SessionId)),
                    c.KeptGivenBack.Count > 0 ? AllocationEndpoints.SeatText(activities, c.KeptGivenBack) : null)).ToList(),
                mails, slot.Phase == Phase.Published));
        });

        // Goes back to the state after a log entry. Logged per person and as a whole; affected people are mailed once they saw their seats.
        admin.MapPost("/slots/{id:guid}/allocation/restore/{logId:guid}", async (Guid id, Guid logId, AppDbContext db, HttpContext http) =>
        {
            if (await db.FairSlots.FindAsync(id) is not { } slot) return Results.NotFound();
            var (error, entry, changes) = await PlanAsync(db, slot, logId);
            if (error is not null) return error;
            var activities = await Database.LoadActivitiesAsync(db, id);
            var changed = changes.Where(c => c.Changed).ToList();
            foreach (var c in changed)
            {
                db.Assignments.RemoveRange(c.Old);
                db.Assignments.AddRange(c.New);
                c.Ballot.Waitlist = c.Waitlist;
                c.Ballot.NoReplacement = c.NoReplacement;
                c.Ballot.GiveUp = c.GiveUp;
                if (!c.SeatsChanged) continue;
                var before = c.Old.Select(a => a.SessionId).ToHashSet();
                var after = c.New.Select(a => a.SessionId).ToHashSet();
                AllocationEndpoints.Log(db, http, id, "seats", c.Ballot.Name, AllocationEndpoints.SeatText(activities, before), AllocationEndpoints.SeatText(activities, after));
                if (slot.Phase is Phase.Published or Phase.Closed)
                    await AllocationEndpoints.SeatsChangedMailAsync(db, slot, c.Ballot, activities, before, after, http);
            }
            AllocationEndpoints.Log(db, http, id, "restored", null, Database.Utc(entry!.At).ToString("o"),
                changed.Count(c => c.SeatsChanged).ToString());
            await db.SaveChangesAsync();
            await MovingUp.FillAsync(db, slot);
            return Results.Ok(await AllocationEndpoints.ForAccountAsync(db, slot, http));
        });
    }

    /// <summary>Compares the save point of a log entry with the current state. Loads ballots and seats tracked, ready to change.</summary>
    static async Task<(IResult? Error, AllocationLogEntity? Entry, List<Change> Changes)> PlanAsync(AppDbContext db, FairSlotEntity slot, Guid logId)
    {
        var entry = await db.AllocationLog.AsNoTracking().FirstOrDefaultAsync(l => l.Id == logId && l.FairSlotId == slot.Id);
        var point = await db.AllocationSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.LogId == logId && s.FairSlotId == slot.Id);
        if (entry is null || point is null) return (Results.NotFound(), null, []);
        if (slot.FrozenAt is null || slot.Phase is not (Phase.Allocation or Phase.Published or Phase.Closed)) return (Endpoints.Errors("restoreClosed"), null, []);
        if (entry.At < await LastFreezeAsync(db, slot.Id)) return (Endpoints.Errors("restoreOld"), null, []);

        var states = (JsonSerializer.Deserialize<List<BallotState>>(point.Data) ?? []).ToDictionary(s => s.Ballot);
        var activityOf = (await db.Sessions.AsNoTracking().Where(s => db.Activities.Any(a => a.Id == s.ActivityId && a.FairSlotId == slot.Id))
            .Select(s => new { s.Id, s.ActivityId }).ToListAsync()).ToDictionary(s => s.Id, s => s.ActivityId);
        var seats = (await db.Assignments.Where(a => a.FairSlotId == slot.Id).ToListAsync()).ToLookup(a => a.BallotId);
        var changes = new List<Change>();
        foreach (var ballot in await db.Ballots.Where(b => b.FairSlotId == slot.Id).ToListAsync())
        {
            if (!states.TryGetValue(ballot.Id, out var state)) continue;
            var declined = ballot.Declined.ToHashSet();
            // Dates deleted since are left out; seats the person gave back since stay given back.
            var then = state.Seats.Where(activityOf.ContainsKey).ToList();
            var kept = then.Where(s => declined.Contains(activityOf[s])).ToList();
            var seated = then.Except(kept).ToList();
            var seatedActivities = seated.Select(s => activityOf[s]).ToHashSet();
            // Ticks used up by moving up come back; ticks set since stay.
            var waitlist = state.Waitlist.Concat(ballot.Waitlist).Distinct().Where(a => !seatedActivities.Contains(a) && !declined.Contains(a)).ToList();
            // Their seat choices come back as well; a choice made since wins.
            var choices = ballot.GiveUpMap();
            foreach (var x in BallotEntity.ParseGiveUp(state.GiveUp)) choices.TryAdd(x.Key, x.Value);
            var change = new Change(ballot, seats[ballot.Id].ToList(),
                seated.Select(s => new AssignmentEntity { FairSlotId = slot.Id, BallotId = ballot.Id, SessionId = s, Locked = state.Locked.Contains(s) }).ToList(),
                waitlist,
                declined.SetEquals(state.Declined) ? state.NoReplacement : ballot.NoReplacement,
                kept,
                choices.Where(x => waitlist.Contains(x.Key)).Select(x => $"{x.Key}>{x.Value}").ToList());
            if (change.Changed || kept.Count > 0) changes.Add(change);
        }
        return (null, entry, changes);
    }
}
