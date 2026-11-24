using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

/// <summary>
/// Undo for changes to activities, the participant list and the rules (the allocation has save points, see <see cref="SavePoints"/>).
/// A logged change keeps how the things it touched were before (<see cref="AllocationLogEntity.Undo"/>); undoing sets exactly
/// these back and overwrites later changes to them. Activities come back with their dates, the points ballots gave them (as far
/// as each budget allows) and the seats people lost with them (only where they still lack a seat there). An undo never takes a
/// seat away; it is logged and can be undone itself.
/// </summary>
public static class Undo
{
    /// <summary>
    /// What a change touched, as it was before. <paramref name="ActivityIds"/>/<paramref name="ParticipantIds"/>: what exists
    /// after the change; undoing removes those without a state in <paramref name="Activities"/>/<paramref name="Participants"/>.
    /// </summary>
    public sealed record Data(
        List<ActivityState>? Activities = null, List<string>? ActivityIds = null,
        List<ParticipantEntity>? Participants = null, List<Guid>? ParticipantIds = null,
        SettingsState? Settings = null);

    public sealed record ActivityState(ActivityEntity Activity, Dictionary<Guid, int> Points, List<SeatState> Seats);
    public sealed record SeatState(Guid Ballot, string Session, bool Locked);

    /// <summary>Rules and registration settings, with what changing them cuts down (extra seats, friend codes, activity groups).</summary>
    public sealed record SettingsState(int PointBudget, int MinActivities, int MaxPerActivity, int PerParticipant, int MaxExtra, bool RequiredStaffing,
        int Friends, List<GroupEntity> Groups, DateTime? RegistrationDeadline, bool OpenToAll, List<string> Excluded,
        Dictionary<Guid, int> Extra, Dictionary<Guid, string> FriendCodes, Dictionary<string, string> ActivityGroups)
    {
        public Rules ToRules() => new(PointBudget, MinActivities, MaxPerActivity, PerParticipant, 5,
            Groups.OrderBy(g => g.SortOrder).Select(g => g.ToModel()).ToList(), MaxExtra, RequiredStaffing, Friends);
    }

    public static string Json(Data data) => JsonSerializer.Serialize(data);

    /// <summary>The activities as they are now (all without ids), with the points given to them and the seats on their dates.</summary>
    public static async Task<List<ActivityState>> ActivitiesAsync(AppDbContext db, Guid slotId, IReadOnlyCollection<string>? ids = null)
    {
        var query = db.Activities.AsNoTracking().Include(a => a.Sessions).Where(a => a.FairSlotId == slotId);
        if (ids is not null) query = query.Where(a => ids.Contains(a.Id));
        var activities = await query.ToListAsync();
        if (activities.Count == 0) return [];
        var ballots = await db.Ballots.AsNoTracking().Where(b => b.FairSlotId == slotId).ToListAsync();
        var sessionIds = activities.SelectMany(a => a.Sessions).Select(s => s.Id).ToList();
        var seats = await db.Assignments.AsNoTracking().Where(a => a.FairSlotId == slotId && sessionIds.Contains(a.SessionId)).ToListAsync();
        return activities.Select(a =>
        {
            var mine = a.Sessions.Select(s => s.Id).ToHashSet();
            return new ActivityState(a,
                ballots.Where(b => b.Points.Any(p => p.ActivityId == a.Id)).ToDictionary(b => b.Id, b => b.Points.First(p => p.ActivityId == a.Id).Points),
                seats.Where(s => mine.Contains(s.SessionId)).Select(s => new SeatState(s.BallotId, s.SessionId, s.Locked)).ToList());
        }).ToList();
    }

    public static ParticipantEntity Copy(ParticipantEntity p) => new()
    {
        Id = p.Id, FairSlotId = p.FairSlotId, Name = p.Name, Email = p.Email, BirthDate = p.BirthDate, BaptismDate = p.BaptismDate,
        Parish = p.Parish, Token = p.Token, InvitedAt = p.InvitedAt,
    };

    public static async Task<SettingsState> SettingsAsync(AppDbContext db, FairSlotEntity slot)
    {
        var ballots = await db.Ballots.AsNoTracking().Where(b => b.FairSlotId == slot.Id && (b.Extra > 0 || b.Friends != null))
            .Select(b => new { b.Id, b.Extra, b.Friends }).ToListAsync();
        var grouped = await db.Activities.AsNoTracking().Where(a => a.FairSlotId == slot.Id && a.GroupId != null).Select(a => new { a.Id, a.GroupId }).ToListAsync();
        return new SettingsState(slot.PointBudget, slot.MinActivities, slot.MaxPerActivity, slot.PerParticipant, slot.MaxExtra, slot.RequiredStaffing, slot.Friends,
            slot.Groups.Select(g => new GroupEntity { Id = g.Id, Name = g.Name, PerParticipant = g.PerParticipant, MinActivities = g.MinActivities, Color = g.Color, SortOrder = g.SortOrder }).ToList(),
            slot.RegistrationDeadline, slot.OpenToAll, slot.Excluded.ToList(),
            ballots.Where(b => b.Extra > 0).ToDictionary(b => b.Id, b => b.Extra),
            ballots.Where(b => b.Friends != null).ToDictionary(b => b.Id, b => b.Friends!),
            grouped.ToDictionary(a => a.Id, a => a.GroupId!));
    }

    public static void MapUndo(this RouteGroupBuilder admin) =>
        admin.MapPost("/slots/{id:guid}/log/{logId:guid}/undo", async (Guid id, Guid logId, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            var entry = await db.AllocationLog.FirstOrDefaultAsync(l => l.Id == logId && l.FairSlotId == id);
            if (slot is null || entry is null) return Results.NotFound();
            if (entry.Undo is null || JsonSerializer.Deserialize<Data>(entry.Undo) is not { } data) return Endpoints.Errors("undoGone");

            var (error, redo, then) =
                data.Settings is { } settings ? await RestoreSettingsAsync(db, slot, settings)
                : data.Participants is not null || data.ParticipantIds is not null ? await RestoreParticipantsAsync(db, http, slot, data)
                : await RestoreActivitiesAsync(db, http, slot, data);
            if (error is not null) return error;
            entry.Undo = null;
            AllocationEndpoints.Log(db, http, id, "undone", entry.Person, entry.Kind, Database.Utc(entry.At).ToString("o"), redo);
            await db.SaveChangesAsync();
            if (then is not null) await then();
            return Results.NoContent();
        });

    record Outcome(IResult? Error, string? Redo = null, Func<Task>? Then = null);

    static async Task<Outcome> RestoreSettingsAsync(AppDbContext db, FairSlotEntity slot, SettingsState s)
    {
        var rules = s.ToRules();
        var rulesChanged = slot.ToRules() != rules;
        if (rulesChanged && slot.FrozenAt is not null) return new(Endpoints.Errors("allocationFrozen"));
        if (slot.Phase == Phase.Open && s.RegistrationDeadline != slot.RegistrationDeadline && s.RegistrationDeadline is { } deadline && deadline <= Phases.Now())
            return new(Endpoints.Errors("deadlinePassed"));
        var redo = Json(new(Settings: await SettingsAsync(db, slot)));

        slot.Apply(rules);
        (slot.RegistrationDeadline, slot.OpenToAll, slot.Excluded) = (s.RegistrationDeadline, s.OpenToAll, s.Excluded);
        // What the change cut down comes back; values set since stay.
        var ballots = await db.Ballots.Where(b => b.FairSlotId == slot.Id).ToListAsync();
        foreach (var b in ballots)
        {
            if (s.Extra.TryGetValue(b.Id, out var extra) && extra > b.Extra) b.Extra = Math.Min(extra, rules.MaxExtra);
            if (b.Friends is null && rules.Friends >= 2 && s.FriendCodes.TryGetValue(b.Id, out var code)) b.Friends = code;
        }
        foreach (var b in ballots.Where(b => b.Friends != null).GroupBy(b => b.Friends).Where(g => rules.Friends < 2 || g.Count() > rules.Friends).SelectMany(g => g))
            b.Friends = null;
        var groupIds = rules.GroupList.Select(g => g.Id).ToHashSet();
        foreach (var a in await db.Activities.Where(a => a.FairSlotId == slot.Id).ToListAsync())
        {
            if (a.GroupId is not null && !groupIds.Contains(a.GroupId)) a.GroupId = null;
            if (a.GroupId is null && s.ActivityGroups.TryGetValue(a.Id, out var group) && groupIds.Contains(group)) a.GroupId = group;
        }
        return new(null, redo, rulesChanged ? () => Database.ResetSamplesAsync(db, slot.Id) : null);
    }

    static async Task<Outcome> RestoreParticipantsAsync(AppDbContext db, HttpContext http, FairSlotEntity slot, Data data)
    {
        var states = data.Participants ?? [];
        var keep = states.Select(p => p.Id).ToHashSet();
        var touched = keep.Concat(data.ParticipantIds ?? []).ToHashSet();
        var current = await db.Participants.Where(p => p.FairSlotId == slot.Id && touched.Contains(p.Id)).ToListAsync();
        var access = Access.Slot(http);
        if (current.Any(p => !access.InScope(p.Parish)) || states.Any(p => !access.InScope(p.Parish))) return new(Endpoints.Errors("parishNotOwn"));
        var others = await db.Participants.Where(p => p.FairSlotId == slot.Id && !touched.Contains(p.Id)).Select(p => p.Name).ToListAsync();
        if (states.Any(s => others.Contains(s.Name, StringComparer.OrdinalIgnoreCase))) return new(Endpoints.Errors("alreadyListed"));
        var redo = Json(new(Participants: current.Select(Copy).ToList(), ParticipantIds: keep.ToList()));

        db.Participants.RemoveRange(current.Where(p => !keep.Contains(p.Id)));
        foreach (var s in states)
        {
            if (current.FirstOrDefault(p => p.Id == s.Id) is { } p)
                (p.Name, p.Email, p.BirthDate, p.BaptismDate, p.Parish, p.Token, p.InvitedAt) = (s.Name, s.Email, s.BirthDate, s.BaptismDate, s.Parish, s.Token, s.InvitedAt);
            else
                db.Participants.Add(Copy(s));
        }
        return new(null, redo);
    }

    static async Task<Outcome> RestoreActivitiesAsync(AppDbContext db, HttpContext http, FairSlotEntity slot, Data data)
    {
        var states = (data.Activities ?? []).ToDictionary(s => s.Activity.Id);
        var touched = states.Keys.Concat(data.ActivityIds ?? []).ToHashSet();
        var current = await db.Activities.Include(a => a.Sessions).Where(a => a.FairSlotId == slot.Id && touched.Contains(a.Id)).ToListAsync();
        var target = states.Values.SelectMany(s => s.Activity.Sessions).Select(s => s.Id).ToHashSet();
        var removedSessions = current.SelectMany(a => a.Sessions).Select(s => s.Id).Where(s => !target.Contains(s)).ToList();
        // An undo never takes a seat away: such seats have to be moved by hand first.
        if (await db.Assignments.AnyAsync(a => a.FairSlotId == slot.Id && removedSessions.Contains(a.SessionId))) return new(Endpoints.Errors("undoSeats"));
        var redo = Json(new(await ActivitiesAsync(db, slot.Id, current.Select(a => a.Id).ToList()), states.Keys.ToList()));

        var removed = current.Where(a => !states.ContainsKey(a.Id)).Select(a => a.Id).ToHashSet();
        var recreated = states.Values.Where(s => current.All(a => a.Id != s.Activity.Id)).ToList();
        var ballots = await db.Ballots.Where(b => b.FairSlotId == slot.Id).ToListAsync();
        foreach (var b in ballots) b.Points.RemoveAll(p => removed.Contains(p.ActivityId));
        db.Activities.RemoveRange(current.Where(a => removed.Contains(a.Id)));

        var groupIds = slot.ToRules().GroupList.Select(g => g.Id).ToHashSet();
        var existingSessions = current.SelectMany(a => a.Sessions).Select(s => s.Id).ToHashSet();
        foreach (var state in states.Values)
        {
            var s = state.Activity;
            var groupId = s.GroupId is not null && groupIds.Contains(s.GroupId) ? s.GroupId : null;
            if (current.FirstOrDefault(a => a.Id == s.Id) is not { } entity)
            {
                db.Activities.Add(new ActivityEntity
                {
                    Id = s.Id, FairSlotId = slot.Id, Title = s.Title, RestrictedTo = s.RestrictedTo, Popularity = s.Popularity, GroupId = groupId,
                    Description = s.Description, Parish = s.Parish, Leader = s.Leader, LeaderEmail = s.LeaderEmail, SortOrder = s.SortOrder,
                    Sessions = s.Sessions.Select(x => new SessionEntity { Id = x.Id, ActivityId = s.Id, Start = x.Start, End = x.End, Location = x.Location, Capacity = x.Capacity, Flexible = x.Flexible }).ToList(),
                });
                continue;
            }
            (entity.Title, entity.RestrictedTo, entity.Popularity, entity.GroupId, entity.Description, entity.Parish, entity.Leader, entity.LeaderEmail, entity.SortOrder) =
                (s.Title, s.RestrictedTo, s.Popularity, groupId, s.Description, s.Parish, s.Leader, s.LeaderEmail, s.SortOrder);
            var keep = new List<SessionEntity>();
            foreach (var x in s.Sessions)
            {
                var session = entity.Sessions.FirstOrDefault(e => e.Id == x.Id) ?? new SessionEntity { Id = x.Id, ActivityId = entity.Id };
                (session.Start, session.End, session.Location, session.Capacity, session.Flexible) = (x.Start, x.End, x.Location, x.Capacity, x.Flexible);
                keep.Add(session);
            }
            entity.Sessions.RemoveAll(e => !keep.Contains(e));
            foreach (var session in keep.Where(e => !entity.Sessions.Contains(e))) entity.Sessions.Add(session);
        }
        // Points come back to re-created activities, as far as each budget allows (a person who voted again keeps that vote).
        foreach (var state in recreated)
        foreach (var b in ballots)
            if (state.Points.TryGetValue(b.Id, out var points) && b.Points.All(p => p.ActivityId != state.Activity.Id) && b.Points.Sum(p => p.Points) + points <= slot.PointBudget)
                b.Points.Add(new BallotPoints { ActivityId = state.Activity.Id, Points = points });

        var seats = states.Values.SelectMany(s => s.Seats).Where(x => !existingSessions.Contains(x.Session)).ToList();
        var changesActivities = removed.Count > 0 || recreated.Count > 0;
        return new(null, redo, async () =>
        {
            if (slot.FrozenAt is null)
            {
                if (changesActivities) await Database.ResetSamplesAsync(db, slot.Id); // sample ballots must refer to the activities
                return;
            }
            if (seats.Count > 0) await RestoreSeatsAsync(db, http, slot, seats);
            await MovingUp.FillAsync(db, slot);
        });
    }

    /// <summary>Seats on re-created dates come back to people who still lack a seat there (not given back, not in the activity already).</summary>
    static async Task RestoreSeatsAsync(AppDbContext db, HttpContext http, FairSlotEntity slot, List<SeatState> seats)
    {
        var activities = await Database.LoadActivitiesAsync(db, slot.Id);
        var ballots = await db.Ballots.Where(b => b.FairSlotId == slot.Id).ToListAsync();
        var models = ballots.ToDictionary(b => b.Id, b => b.ToModel());
        var current = await db.Assignments.AsNoTracking().Where(a => a.FairSlotId == slot.Id).ToListAsync();
        var allocation = Allocator.FromAssignments(activities, models.Values.ToList(), current.Select(a => a.ToModel()), slot.ToRules().Groups);
        var sessions = activities.SelectMany(a => a.Sessions).ToDictionary(s => s.Id);
        var gained = new Dictionary<Guid, List<string>>();
        foreach (var seat in seats)
        {
            if (!sessions.TryGetValue(seat.Session, out var session) || !models.TryGetValue(seat.Ballot, out var model)) continue;
            if (model.HasDeclined(session.ActivityId) || allocation.Assignments[model.Id].Any(x => x.Session.ActivityId == session.ActivityId)) continue;
            var group = allocation.GroupKey(session.ActivityId);
            if (!allocation.MissingSeats(model, slot.PerParticipant).Any(m => (m.Group?.Id ?? "") == group)) continue;
            allocation.Add(model.Id, session);
            db.Assignments.Add(new AssignmentEntity { FairSlotId = slot.Id, BallotId = seat.Ballot, SessionId = seat.Session, Locked = seat.Locked });
            (gained.TryGetValue(seat.Ballot, out var list) ? list : gained[seat.Ballot] = []).Add(seat.Session);
        }
        foreach (var (ballotId, added) in gained)
        {
            var ballot = ballots.Single(b => b.Id == ballotId);
            var had = current.Where(a => a.BallotId == ballotId).Select(a => a.SessionId).ToHashSet();
            var now = had.Concat(added).ToHashSet();
            AllocationEndpoints.Log(db, http, slot.Id, "seats", ballot.Name, AllocationEndpoints.SeatText(activities, had), AllocationEndpoints.SeatText(activities, now));
            if (slot.Phase is Phase.Published or Phase.Closed)
                await AllocationEndpoints.SeatsChangedMailAsync(db, slot, ballot, activities, had, now, http);
        }
        await db.SaveChangesAsync();
    }
}
