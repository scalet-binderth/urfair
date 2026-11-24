using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record AssignmentDto(string ActivityId, string Title, string? GroupId, string SessionId, DateTime Start, DateTime? End, bool Flexible, string Location, int Points,
    bool IsFallback, bool Locked);
/// <param name="NeedsReplacement">A real person who gave back or cancelled a seat, wants a replacement and still lacks a seat.</param>
/// <param name="Suggestions">For <paramref name="NeedsReplacement"/>: seats organisers could give them with one click.</param>
public record ParticipantDto(string BallotId, string Name, bool IsSample, double Satisfaction, IReadOnlyList<AssignmentDto> Assignments,
    IReadOnlyDictionary<string, int> Points, IReadOnlyList<string> Original, IReadOnlyList<string> Waitlist, IReadOnlyList<string> Declined,
    bool NeedsReplacement = false, IReadOnlyList<SuggestionDto>? Suggestions = null);
/// <param name="Kind">otherDate (another date of an activity they gave back), pointed (free, with points), free (free, without points) or overbook (full, most points).</param>
public record SuggestionDto(string SessionId, string ActivityId, string Title, DateTime Start, bool Flexible, int Points, string Kind);
public record AllocationDto(int PerParticipant, Metrics PointBased, Metrics FirstComeFirstServed,
    IReadOnlyDictionary<string, int> SessionLoad, IReadOnlyList<ParticipantDto> Participants, DateTime? FrozenAt, bool WasVisible = false);
public record SeatRequest(string SessionId, bool Locked);
public record SeatsRequest(List<SeatRequest>? Seats);
public record LogDto(DateTime At, string Actor, string Kind, string? Person, string? Before, string? After, Guid Id = default, bool CanRestore = false, bool CanUndo = false);
public record ListPersonDto(string Name, string? Parish, string? Email, DateOnly? BirthDate, DateOnly? BaptismDate);
public record ListSessionDto(string Id, DateTime Start, DateTime? End, bool Flexible, string Location, int? Capacity, IReadOnlyList<ListPersonDto> People);
public record ListActivityDto(string Id, string Title, string? Leader, string? LeaderEmail, string? GroupId, IReadOnlyList<ListSessionDto> Sessions);
public record LeaderListsDto(bool Frozen, IReadOnlyList<ListActivityDto> Activities);
public record LeaderMailRequest(List<string>? ActivityIds, bool WithEmail, bool WithBirthDate, bool WithBaptismDate = false);

/// <summary>
/// The allocation of a FairSlot. During draft and registration it is calculated on every request (live preview).
/// With the allocation phase (at the deadline or by hand) it is frozen; afterwards organisers move people by hand,
/// fix seats and recalculate the rest, and participants see exactly the stored result once it is published. Going back
/// to registration unfreezes it. Freezing also keeps the original result, and every change is logged, so organisers
/// can see what a person had before and go back to it.
/// </summary>
public static class AllocationEndpoints
{
    public static void MapAllocationAdmin(this RouteGroupBuilder admin)
    {
        admin.MapGet("/slots/{id:guid}/allocation", async (Guid id, AppDbContext db, HttpContext http) =>
            await db.FairSlots.FindAsync(id) is { } slot ? Results.Ok(await ForAccountAsync(db, slot, http)) : Results.NotFound());

        // Fixed seats stay, everything else is allocated again. The original from freezing is kept. Only while internal:
        // once participants see the allocation, free seats move up instead (MovingUp).
        admin.MapPost("/slots/{id:guid}/allocation/recalculate", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.FrozenAt is null) return Endpoints.Errors("notFrozen");
            if (slot.Phase != Phase.Allocation) return Endpoints.Errors("recalculateInternal");
            if (await WasVisibleAsync(db, id)) return Endpoints.Errors("wasVisible");
            var locked = await db.Assignments.Where(a => a.FairSlotId == id && a.Locked).ToListAsync();
            Log(db, http, id, "recalculated");
            await FreezeAsync(db, slot, locked.Select(a => a.ToModel()).ToList());
            return Results.Ok(await ForAccountAsync(db, slot, http));
        });

        // All seats of one participant, set by hand: move, add, remove, fix. Full sessions may be overbooked on purpose.
        admin.MapPut("/slots/{id:guid}/allocation/{ballotId:guid}", async (Guid id, Guid ballotId, SeatsRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            var ballot = await db.Ballots.FirstOrDefaultAsync(b => b.Id == ballotId && b.FairSlotId == id);
            if (slot is null || ballot is null) return Results.NotFound();
            if (!(await Access.SeesPersonAsync(http, db, id))(ballot.Name)) return Results.NotFound();
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
            // What organisers set counts: a seat they leave missing is not filled on its own (the person can still take a free one).
            var model = Allocator.FromAssignments(activities, [ballot.ToModel()],
                seats.Select(s => new FixedAssignment(ballot.Id.ToString(), s.SessionId)), slot.ToRules().Groups);
            ballot.NoReplacement = model.MissingSeats(ballot.ToModel(), slot.PerParticipant).Select(m => m.Group?.Id ?? "").ToList();
            if (before != after) Log(db, http, id, "seats", ballot.Name, before, after);
            if (before != after && slot.Phase is (Phase.Published or Phase.Closed))
                await SeatsChangedMailAsync(db, slot, ballot, activities, old.Select(a => a.SessionId).ToHashSet(), seats.Select(s => s.SessionId).ToHashSet(), http);
            await db.SaveChangesAsync();
            await MovingUp.FillAsync(db, slot);
            return Results.Ok(await ForAccountAsync(db, slot, http));
        });

        // Changes to the frozen allocation, newest first. Entries with a save point can be gone back to (SavePoints),
        // changes to activities, participants and rules can be undone (Undo).
        admin.MapGet("/slots/{id:guid}/allocation/log", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            if (await db.FairSlots.FindAsync(id) is not { } slot) return Results.NotFound();
            var sees = await Access.SeesPersonAsync(http, db, id);
            var restorable = await SavePoints.RestorableAsync(db, slot);
            return Results.Ok((await db.AllocationLog.AsNoTracking().Where(l => l.FairSlotId == id)
                    .Select(l => new { l.Id, l.At, l.Actor, l.Kind, l.Person, l.Before, l.After, CanUndo = l.Undo != null }).ToListAsync())
                .OrderByDescending(l => l.At).Where(l => sees(l.Person)).Take(300)
                .Select(l => new LogDto(Database.Utc(l.At), l.Actor, l.Kind, l.Person, l.Before, l.After, l.Id, restorable.Contains(l.Id), l.CanUndo)));
        });
        admin.MapSavePoints();
        admin.MapUndo();
        admin.MapSeatOfferAdmin();
        admin.MapLeaders();

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
            var mails = await LeaderMailsAsync(db, slot, chosen, req);
            await db.SaveChangesAsync();
            return Results.Ok(new { created = mails, withoutEmail = chosen.Count(a => a.LeaderEmail is null) });
        });
    }

    /// <summary>
    /// One mail per leader e-mail address with the lists of their activities; what they got is remembered, so the organisers
    /// see later changes and can mail them (<see cref="LeaderNotices"/>). Saved by the caller.
    /// </summary>
    public static async Task<int> LeaderMailsAsync(AppDbContext db, FairSlotEntity slot, List<ListActivityDto> chosen, LeaderMailRequest req)
    {
        var withEmail = chosen.Where(a => a.LeaderEmail is not null).ToList();
        var mails = withEmail.GroupBy(a => a.LeaderEmail!.ToLowerInvariant()).Select(g => LeaderMail(slot, g.ToList(), req)).ToList();
        foreach (var mail in mails) Mailing.Add(db, slot, mail);
        var lists = withEmail.SelectMany(a => a.Sessions).ToDictionary(s => s.Id, s => LeaderNotices.ListText(s.People.Select(p => p.Name)));
        foreach (var s in await db.Sessions.Where(s => lists.Keys.Contains(s.Id)).ToListAsync()) s.LeaderList = lists[s.Id];
        return mails.Count;
    }

    /// <summary>
    /// The lists per activity and date. E-mail address and dates of birth and baptism only of participants in the account's own
    /// Pfarrgemeinden (as in the participant list). Without a leader the vote's organiser is named, as on the activity, and
    /// their address is used.
    /// </summary>
    public static async Task<LeaderListsDto> ListsAsync(AppDbContext db, FairSlotEntity slot, SlotAccess access)
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
        return new LeaderListsDto(slot.FrozenAt is not null, activities.Select(a => new ListActivityDto(a.Id, a.Title, a.Leader ?? slot.Organiser, Leaders.EmailOf(a.Leader, a.LeaderEmail, slot), a.GroupId,
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
        body.Append("\nÄndert sich noch etwas, melden wir uns mit den Änderungen.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
        return new MailEntity { To = activities[0].LeaderEmail!, Subject = $"Teilnahmeliste: {slot.Name}", Body = body.ToString() };
    }

    static string When(ListSessionDto s) => When(s.Start, s.End, s.Flexible);

    /// <summary>A date as text for mails, e.g. "14.07.2026, 09:00–13:00 Uhr".</summary>
    public static string When(DateTime start, DateTime? end, bool flexible) =>
        flexible ? $"bis {start:dd.MM.yyyy}, nach Vereinbarung"
        : end is not { } e ? $"{start:dd.MM.yyyy, HH:mm} Uhr"
        : e.Date == start.Date ? $"{start:dd.MM.yyyy, HH:mm}–{e:HH:mm} Uhr"
        : $"{start:dd.MM.yyyy, HH:mm} Uhr bis {e:dd.MM.yyyy, HH:mm} Uhr";

    /// <summary>Adds a log entry; it is saved with the next SaveChanges.</summary>
    /// <param name="undo">How the things the change touched were before (<see cref="Undo"/>), so it can be undone.</param>
    public static void Log(AppDbContext db, HttpContext http, Guid slotId, string kind, string? person = null, string? before = null, string? after = null, string? undo = null) =>
        Log(db, slotId, Access.Account(http).Name, kind, person, before, after, undo);

    /// <summary>Logs a change to activities, participants or settings once the FairSlot is no longer a draft (only then it can be undone).</summary>
    public static async Task LogChangeAsync(AppDbContext db, HttpContext http, Guid slotId, string kind, string? person = null, string? before = null, string? after = null,
        string? undo = null)
    {
        if (await db.FairSlots.AsNoTracking().Where(s => s.Id == slotId).Select(s => (Phase?)s.Phase).SingleOrDefaultAsync() is { } phase && phase != Phase.Draft)
            Log(db, http, slotId, kind, person, before, after, undo);
    }

    /// <summary>Seats of an activity for the log ("∞" if a date has no limit).</summary>
    public static string SeatsText(IEnumerable<SessionEntity> sessions) =>
        sessions.Any(s => s.Capacity is null) ? "∞" : sessions.Sum(s => s.Capacity ?? 0).ToString();

    /// <summary>Adds a log entry with a given actor (a participant, or Urfair for automatic changes).</summary>
    public static void Log(AppDbContext db, Guid slotId, string actor, string kind, string? person = null, string? before = null, string? after = null, string? undo = null) =>
        db.AllocationLog.Add(new AllocationLogEntity { FairSlotId = slotId, Actor = actor, Kind = kind, Person = person, Before = before, After = after, Undo = undo });

    /// <summary>Once participants see the allocation, a change by hand is mailed to the person (if on the list with an e-mail address).</summary>
    public static async Task SeatsChangedMailAsync(AppDbContext db, FairSlotEntity slot, BallotEntity ballot, List<Activity> activities,
        HashSet<string> before, HashSet<string> after, HttpContext http)
    {
        if (ballot.IsSample) return;
        if (await Database.ListedAsync(db, slot.Id, ballot.Name) is not { } p) return;
        var gained = SeatText(activities, after.Except(before));
        var lost = SeatText(activities, before.Except(after));
        var body = new StringBuilder($"bei „{slot.Name}“ hat das Organisationsteam deine Plätze geändert.\n");
        if (gained.Length > 0) body.Append($"Neu: {gained}\n");
        if (lost.Length > 0) body.Append($"Nicht mehr: {lost}\n");
        body.Append($"\nDeine Plätze jetzt: {(after.Count > 0 ? SeatText(activities, after) : "keine")}\n");
        Mailing.ToParticipant(db, slot, p, $"Deine Plätze: {slot.Name}", body.ToString(),
            "Alle Termine siehst du unter deinem persönlichen Link", Mailing.BaseUrl(http.Request));
    }

    /// <summary>Seats as readable text for the log: activity titles in date order, with the date where an activity has several.</summary>
    public static string SeatText(List<Activity> activities, IEnumerable<string> sessionIds)
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

    /// <summary>The allocation as the signed-in account may see it: people of other Pfarrgemeinden left out, seat counts complete.</summary>
    public static async Task<AllocationDto> ForAccountAsync(AppDbContext db, FairSlotEntity slot, HttpContext http)
    {
        var result = await AllocateAsync(db, slot);
        var sees = await Access.SeesPersonAsync(http, db, slot.Id);
        return result with { Participants = result.Participants.Where(p => sees(p.Name)).ToList() };
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
        var entity = entities.ToDictionary(b => b.Id.ToString());
        var original = slot.FrozenAt is null ? [] : (await db.OriginalAssignments.AsNoTracking().Where(a => a.FairSlotId == slot.Id).ToListAsync())
            .GroupBy(a => a.BallotId.ToString()).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(a => a.SessionId).ToList());

        var reserved = await ReservedAsync(db, slot);
        var now = Phases.Now();
        var participants = ballots.Select(b =>
        {
            var needs = !isSample[b.Id] && entity[b.Id].Declined.Count > 0 && allocation.MissingSeats(b, k).Any(m => b.WantsReplacement(m.Group?.Id ?? ""));
            return new ParticipantDto(b.Id, b.Name, isSample[b.Id], allocation.Satisfaction(b, k),
                allocation.Assignments[b.Id].OrderBy(x => x.Session.Start).Select(x => new AssignmentDto(
                    x.Session.ActivityId, titles[x.Session.ActivityId], groupOf[x.Session.ActivityId], x.Session.Id, x.Session.Start, x.Session.End,
                    x.Session.Flexible, x.Session.Location, x.Points, x.IsFallback, x.Locked)).ToList(),
                b.Points, original.GetValueOrDefault(b.Id, []), entity[b.Id].Waitlist, entity[b.Id].Declined,
                needs, needs ? Suggest() : null);

            // Free seats first (another date of what they gave back, then by points), then overbooking their best wish.
            IReadOnlyList<SuggestionDto> Suggest()
            {
                var mine = original.GetValueOrDefault(b.Id, []);
                var options = activities
                    .Where(a => a.OpenTo(b.Gender))
                    .Where(a => !b.HasDeclined(a.Id) || a.Sessions.Count > 1)
                    .SelectMany(a => a.Sessions)
                    .Where(s => s.Start > now && allocation.CanAdd(b, s, k) && !(b.HasDeclined(s.ActivityId) && mine.Contains(s.Id)))
                    .Select(s => (Session: s, Free: s.Capacity is { } c ? c - allocation.Load(s) - reserved.GetValueOrDefault(s.Id) : int.MaxValue,
                        Points: b.PointsFor(s.ActivityId), Declined: b.HasDeclined(s.ActivityId)))
                    .ToList();
                SuggestionDto Dto((Session Session, int Free, int Points, bool Declined) o, string kind) =>
                    new(o.Session.Id, o.Session.ActivityId, titles[o.Session.ActivityId], o.Session.Start, o.Session.Flexible, o.Points, kind);
                return [
                    .. options.Where(o => o.Free > 0).OrderByDescending(o => o.Declined).ThenByDescending(o => o.Points).ThenBy(o => o.Session.Start).Take(3)
                        .Select(o => Dto(o, o.Declined ? "otherDate" : o.Points > 0 ? "pointed" : "free")),
                    .. options.Where(o => o.Free <= 0 && o.Points > 0 && !o.Declined).OrderByDescending(o => o.Points).ThenBy(o => o.Session.Start).Take(1)
                        .Select(o => Dto(o, "overbook")),
                ];
            }
        }).ToList();

        var load = activities.SelectMany(a => a.Sessions).ToDictionary(x => x.Id, allocation.Load);
        // Stored as UTC; the database returns it without a kind.
        DateTime? frozen = slot.FrozenAt is { } f ? Database.Utc(f) : null;
        return new AllocationDto(k, allocation.Evaluate(k), fcfs.Evaluate(k), load, participants, frozen, await WasVisibleAsync(db, slot.Id));
    }

    /// <summary>Seats held for open offers (<see cref="SeatOffers"/>), per session.</summary>
    public static async Task<Dictionary<string, int>> ReservedAsync(AppDbContext db, FairSlotEntity slot) =>
        (await db.SeatOffers.AsNoTracking().Where(o => o.FairSlotId == slot.Id && o.Status == OfferStatus.Open).Select(o => o.SessionId).ToListAsync())
            .CountBy(x => x).ToDictionary();

    /// <summary>
    /// Participants have seen the allocation once (the log shows a visible phase or their own changes). Then it is not
    /// recalculated and the vote does not go back to registration: seats people know about stay.
    /// </summary>
    public static async Task<bool> WasVisibleAsync(AppDbContext db, Guid slotId)
    {
        string[] visible = [nameof(Phase.Published), nameof(Phase.Closed), nameof(Phase.Ended)];
        string[] byParticipants = ["movedUp", "givenBack", "cancelled", "tookSeat"];
        return await db.AllocationLog.AnyAsync(l => l.FairSlotId == slotId &&
            ((l.Kind == "phase" && (visible.Contains(l.Before!) || visible.Contains(l.After!))) || byParticipants.Contains(l.Kind)));
    }
}
