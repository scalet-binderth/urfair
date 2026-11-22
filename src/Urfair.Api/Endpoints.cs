using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record SessionDto(string Id, DateTime Start, DateTime? End, string Location, int? Capacity, bool Flexible);
public record ActivityDto(string Id, string Title, string? RestrictedTo, int? Seats, IReadOnlyList<SessionDto> Sessions, string? GroupId, string? Description, string? Parish, string? Leader, string? LeaderEmail = null);
public record SlotSummaryDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, int ActivityCount, int BallotCount);
public record SlotDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, Rules Rules, int BallotCount, int SampleCount, bool AdminProtected, bool OpenToAll, string? Parish, string? Organiser,
    Visibility Visibility, bool ShowInterim = false, bool? CanEdit = null);
public record BallotRequest(string Name, Dictionary<string, int> Points, List<string>? UnavailableSessions);
public record ErrorsDto(IReadOnlyList<ValidationError> Errors);
public record LookupRequest(string Name);
public record WaitlistRequest(string Name, List<string>? ActivityIds);
public record GiveBackRequest(string Name, string ActivityId, bool Replacement);
public record TakeSeatRequest(string Name, string SessionId);
/// <param name="Status">fixed, waitlist (full), free (seats left) or givenBack.</param>
/// <param name="CanMoveUp">While visible: a tick can move the person up (more points than the seat they would give up, <paramref name="GivesUp"/>).</param>
public record MyActivityStatusDto(string ActivityId, string Title, string? GroupId, int Points, string Status, int? Position, bool Waitlisted,
    DateTime? Start, DateTime? End, bool Flexible, string? Location, bool CanMoveUp = false, string? GivesUp = null, bool CanGiveBack = false);
/// <summary>Seats a person lacks in a group (null without groups), with the free seats they may take.</summary>
public record MissingDto(string? GroupId, int Count, IReadOnlyList<OpenSeatDto> Open);
public record OpenSeatDto(string SessionId, string ActivityId, string Title, DateTime Start, DateTime? End, bool Flexible, string Location, int Points);
public record MyStatusDto(bool Voted, IReadOnlyList<MyActivityStatusDto> Activities, IReadOnlyList<string> Full, IReadOnlyList<MissingDto>? Missing = null);

/// <summary>Endpoints for participants.</summary>
public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Tells the start page whether the organisation area needs an access key.
        api.MapGet("/info", (IConfiguration cfg) => new { adminProtected = !string.IsNullOrEmpty(cfg["Admin:Key"]), demo = Database.IsDemo(cfg), parishes = Parishes.All });

        // Reloads the own previously submitted ballot so it can be changed.
        api.MapPost("/slots/{id:guid}/ballots/lookup", async (Guid id, LookupRequest req, AppDbContext db, HttpContext http) =>
        {
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } who) return Results.Unauthorized();
            var name = who.ToLower();
            var ballot = await db.Ballots.Include(b => b.Points).AsNoTracking()
                .Where(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name)
                .OrderByDescending(b => b.SubmittedAt).FirstOrDefaultAsync();
            return ballot is null
                ? Results.NotFound()
                : Results.Ok(new { points = ballot.Points.ToDictionary(p => p.ActivityId, p => p.Points), unavailableSessions = ballot.UnavailableSessions });
        });

        // FairSlots this person is listed in (drafts stay hidden). Identified by personal link, in demo mode also by name.
        api.MapPost("/my-slots", async (LookupRequest req, AppDbContext db, HttpContext http) =>
        {
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } who) return Results.Unauthorized();
            var name = who.ToLower();
            return Results.Ok(await db.FairSlots
                .Where(s => s.Phase != Phase.Draft && (s.Participants.Any(p => p.Name.ToLower() == name) || (s.OpenToAll && name != DemoData.NobodyName)))
                .OrderBy(s => s.Phase).ThenBy(s => s.RegistrationDeadline)
                .Select(s => new SlotSummaryDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline, s.Activities.Count, s.Ballots.Count))
                .ToListAsync());
        });

        // The own allocation once the organisers have published it.
        api.MapPost("/slots/{id:guid}/my-result", async (Guid id, LookupRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (!Phases.ShowsResult(slot.Phase)) return Errors("notPublished");
            var result = await AllocationEndpoints.AllocateAsync(db, slot);
            var mine = result.Participants.FirstOrDefault(p => !p.IsSample && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return Results.Ok(new { voted = mine is not null, assignments = mine?.Assignments ?? [] });
        });

        // What the person would get if registration closed now (only if the organisers show it), later the published allocation.
        api.MapPost("/slots/{id:guid}/my-status", async (Guid id, LookupRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (!ShowsStatus(slot))
                return Results.Ok(new MyStatusDto(await db.Ballots.AnyAsync(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name.ToLower()), [], []));
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        // Which not-yet-fixed activities the person wants to move up into. While visible, a tick may move them up at once.
        api.MapPost("/slots/{id:guid}/waitlist", async (Guid id, WaitlistRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (!(slot.Phase == Phase.Published || slot.Phase == Phase.Open && slot.ShowInterim)) return Errors("waitlistClosed");
            var ballot = await MyBallotAsync(db, id, name);
            if (ballot is null) return Errors("notVoted");
            var pointed = ballot.Points.Where(p => p.Points > 0).Select(p => p.ActivityId).ToHashSet();
            ballot.Waitlist = (req.ActivityIds ?? []).Where(x => pointed.Contains(x) && !ballot.Declined.Contains(x)).Distinct().ToList();
            await db.SaveChangesAsync();
            await MovingUp.FillAsync(db, slot);
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        // Gives back a seat the person no longer wants (only while visible). It is never allocated to them again; the next
        // person moves up. With a replacement the person gets a free seat of the same group they gave points to, if there is one.
        api.MapPost("/slots/{id:guid}/give-back", async (Guid id, GiveBackRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (slot.Phase != Phase.Published || slot.FrozenAt is null) return Errors("giveBackClosed");
            var ballot = await MyBallotAsync(db, id, name);
            if (ballot is null) return Errors("notVoted");
            var activities = await Database.LoadActivitiesAsync(db, id);
            var sessions = activities.FirstOrDefault(a => a.Id == req.ActivityId)?.Sessions.Select(s => s.Id).ToHashSet() ?? [];
            var seats = await db.Assignments.Where(a => a.BallotId == ballot.Id).ToListAsync();
            if (seats.FirstOrDefault(a => sessions.Contains(a.SessionId)) is not { } seat) return Errors("noSeat");

            var before = AllocationEndpoints.SeatText(activities, seats.Select(a => a.SessionId));
            db.Assignments.Remove(seat);
            var after = AllocationEndpoints.SeatText(activities, seats.Where(a => a != seat).Select(a => a.SessionId));
            ballot.Declined = [.. ballot.Declined.Append(req.ActivityId).Distinct()];
            ballot.Waitlist = ballot.Waitlist.Where(x => x != req.ActivityId).ToList();
            var group = Allocator.FromAssignments(activities, [], [], slot.ToRules().Groups).GroupKey(req.ActivityId);
            ballot.NoReplacement = req.Replacement
                ? ballot.NoReplacement.Where(x => x != group).ToList()
                : [.. ballot.NoReplacement.Append(group).Distinct()];
            AllocationEndpoints.Log(db, id, name, "givenBack", name, before, after);
            await db.SaveChangesAsync();
            await MovingUp.FillAsync(db, slot);
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        // Takes a free seat in a group where the person lacks one (also an activity without points, e.g. as a replacement).
        api.MapPost("/slots/{id:guid}/take-seat", async (Guid id, TakeSeatRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (slot.Phase != Phase.Published || slot.FrozenAt is null) return Errors("giveBackClosed");
            var ballot = await MyBallotAsync(db, id, name);
            if (ballot is null) return Errors("notVoted");
            var (allocation, activities, _) = await AllocationEndpoints.CurrentAsync(db, slot);
            var me = ballot.ToModel();
            if (allocation.OpenSeats(me, slot.PerParticipant).FirstOrDefault(s => s.Id == req.SessionId) is null) return Errors("seatTaken");

            var seats = await db.Assignments.Where(a => a.BallotId == ballot.Id).Select(a => a.SessionId).ToListAsync();
            db.Assignments.Add(new AssignmentEntity { FairSlotId = id, BallotId = ballot.Id, SessionId = req.SessionId });
            AllocationEndpoints.Log(db, id, name, "tookSeat", name, AllocationEndpoints.SeatText(activities, seats),
                AllocationEndpoints.SeatText(activities, seats.Append(req.SessionId)));
            await db.SaveChangesAsync();
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        api.MapGet("/slots/{id:guid}", async (Guid id, AppDbContext db, IConfiguration cfg) =>
            await ToDto(db, id, cfg) is { } dto ? Results.Ok(dto) : Results.NotFound());

        api.MapGet("/slots/{id:guid}/activities", async (Guid id, AppDbContext db) =>
            (await Database.LoadActivitiesAsync(db, id)).Select(ToDto));

        api.MapPost("/slots/{id:guid}/ballots", async (Guid id, BallotRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.Phase != Phase.Open || Phases.DeadlinePassed(slot)) return Errors("registrationClosed");
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (!await Database.CanTakePartAsync(db, id, name)) return Errors("notListed");

            var activities = await Database.LoadActivitiesAsync(db, id);
            var points = (req.Points ?? []).Where(p => p.Value != 0).ToDictionary();
            var unavailable = (req.UnavailableSessions ?? []).ToHashSet();
            var errors = Validation.Check(new Ballot("new", name, points, unavailable), slot.ToRules(), activities);
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

            // A new ballot of the same person replaces the previous one.
            var existing = await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name.ToLower()).ToListAsync();
            db.Ballots.RemoveRange(existing);
            var entity = new BallotEntity
            {
                FairSlotId = id, Name = name,
                Points = points.Select(p => new BallotPoints { ActivityId = p.Key, Points = p.Value }).ToList(),
                UnavailableSessions = unavailable.ToList(),
                // Keep earlier waiting-list choices for activities that still have points.
                Waitlist = existing.SelectMany(b => b.Waitlist).Where(points.ContainsKey).Distinct().ToList(),
            };
            db.Ballots.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = entity.Id, replaced = existing.Count > 0 });
        });
    }

    public static async Task<SlotDto?> ToDto(AppDbContext db, Guid id, IConfiguration cfg)
    {
        var s = await db.FairSlots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (s is null) return null;
        var ballots = await db.Ballots.CountAsync(b => b.FairSlotId == id);
        return new SlotDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline, s.ToRules(), ballots, s.SampleCount, !string.IsNullOrEmpty(cfg["Admin:Key"]), s.OpenToAll, s.Parish, s.Organiser, s.Visibility, s.ShowInterim);
    }

    public static ActivityDto ToDto(Activity a) => new(a.Id, a.Title, a.RestrictedTo, a.Seats,
        a.Sessions.Select(s => new SessionDto(s.Id, s.Start, s.End, s.Location, s.Capacity, s.Flexible)).ToList(), a.GroupId, a.Description, a.Parish, a.Leader);

    /// <summary>With the leader's e-mail address, which participants do not see.</summary>
    public static ActivityDto ToAdminDto(Activity a) => ToDto(a) with { LeaderEmail = a.LeaderEmail };

    /// <summary>
    /// Uses the valid allocation (stored once frozen, otherwise calculated with the ballots so far). Per activity the person gave points to:
    /// "fixed" (would get it), "waitlist" (all seats taken), "free" (seats left, but the person already has enough higher-ranked
    /// activities, or the date clashes) or "givenBack". The waiting-list position counts people without a seat there who gave more
    /// points; while visible only those who ticked it. While visible it also says what a tick would cost, which seats can be
    /// given back and which seats the person lacks, with the free ones they may take.
    /// </summary>
    private static async Task<MyStatusDto> StatusAsync(AppDbContext db, FairSlotEntity slot, string name)
    {
        var (allocation, activities, entities) = await AllocationEndpoints.CurrentAsync(db, slot);
        var mine = entities.Where(b => !b.IsSample && b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).MaxBy(b => b.SubmittedAt);
        var ballots = entities.Select(b => b.ToModel()).ToList();

        bool Full(Activity a) => a.Sessions.All(s => s.Capacity is { } c && allocation.Load(s) >= c);
        var full = activities.Where(Full).Select(a => a.Id).ToList();
        if (mine is null) return new MyStatusDto(false, [], full);

        var visible = slot.Phase == Phase.Published;
        var me = ballots.Single(b => b.Id == mine.Id.ToString());
        var assigned = allocation.Assignments[me.Id].ToDictionary(x => x.Session.ActivityId);
        var titles = activities.ToDictionary(a => a.Id, a => a.Title);
        var list = activities.Where(a => me.PointsFor(a.Id) > 0 || assigned.ContainsKey(a.Id))
            .OrderByDescending(a => me.PointsFor(a.Id))
            .Select(a =>
            {
                if (assigned.TryGetValue(a.Id, out var x))
                    return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), "fixed", null, false,
                        x.Session.Start, x.Session.End, x.Session.Flexible, x.Session.Location, CanGiveBack: visible);
                if (me.HasDeclined(a.Id))
                    return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), "givenBack", null, false, null, null, false, null);
                var waiting = full.Contains(a.Id);
                int? position = waiting
                    ? 1 + ballots.Count(b => b.Id != me.Id && b.PointsFor(a.Id) > me.PointsFor(a.Id) && (!visible || b.Ticked(a.Id)) &&
                        allocation.Assignments[b.Id].All(y => y.Session.ActivityId != a.Id))
                    : null;
                var (canMoveUp, givesUp) = visible ? allocation.MoveUpFor(me, a, slot.PerParticipant) : (false, null);
                return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), waiting ? "waitlist" : "free", position,
                    mine.Waitlist.Contains(a.Id), null, null, false, null, canMoveUp, givesUp is null ? null : titles[givesUp.ActivityId]);
            }).ToList();
        IReadOnlyList<MissingDto> missing = !visible ? [] : allocation.MissingSeats(me, slot.PerParticipant).Select(m => new MissingDto(m.Group?.Id, m.Count,
            allocation.OpenSeats(me, slot.PerParticipant)
                .Where(s => allocation.GroupKey(s.ActivityId) == (m.Group?.Id ?? ""))
                .OrderByDescending(s => me.PointsFor(s.ActivityId)).ThenBy(s => s.Start)
                .Select(s => new OpenSeatDto(s.Id, s.ActivityId, titles[s.ActivityId], s.Start, s.End, s.Flexible, s.Location, me.PointsFor(s.ActivityId)))
                .ToList())).ToList();
        return new MyStatusDto(true, list, full, missing);
    }

    /// <summary>The person's latest own ballot (tracked, with points).</summary>
    private static Task<BallotEntity?> MyBallotAsync(AppDbContext db, Guid slotId, string name) =>
        db.Ballots.Include(b => b.Points)
            .Where(b => b.FairSlotId == slotId && !b.IsSample && b.Name.ToLower() == name.ToLower())
            .OrderByDescending(b => b.SubmittedAt).FirstOrDefaultAsync();

    /// <summary>Interim status during registration only if the organisers switched it on; the allocation once published.</summary>
    private static bool ShowsStatus(FairSlotEntity slot) => slot.Phase == Phase.Open && slot.ShowInterim || Phases.ShowsResult(slot.Phase);

    public static IResult Errors(string code) => Results.BadRequest(new ErrorsDto([new ValidationError(code)]));
}
