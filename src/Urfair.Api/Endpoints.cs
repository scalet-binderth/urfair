using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record SessionDto(string Id, DateTime Start, DateTime? End, string Location, int? Capacity, bool Flexible);
public record ActivityDto(string Id, string Title, string? RestrictedTo, int? Seats, IReadOnlyList<SessionDto> Sessions, string? GroupId, string? Description);
public record SlotSummaryDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, int ActivityCount, int BallotCount);
public record SlotDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, Rules Rules, int BallotCount, int SampleCount, bool AdminProtected, bool OpenToAll);
public record BallotRequest(string Name, Dictionary<string, int> Points, List<string>? UnavailableSessions);
public record ErrorsDto(IReadOnlyList<ValidationError> Errors);
public record LookupRequest(string Name);
public record WaitlistRequest(string Name, List<string>? ActivityIds);
public record MyActivityStatusDto(string ActivityId, string Title, string? GroupId, int Points, string Status, int? Position, bool Waitlisted,
    DateTime? Start, DateTime? End, bool Flexible, string? Location);
public record MyStatusDto(bool Voted, IReadOnlyList<MyActivityStatusDto> Activities, IReadOnlyList<string> Full);

/// <summary>Endpoints for participants.</summary>
public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Tells the start page whether the organisation area needs an access key.
        api.MapGet("/info", (IConfiguration cfg) => new { adminProtected = !string.IsNullOrEmpty(cfg["Admin:Key"]), demo = Database.IsDemo(cfg) });

        // Demo: reload a previously submitted ballot by name so it can be changed. Replaced by personal links later.
        api.MapPost("/slots/{id:guid}/ballots/lookup", async (Guid id, LookupRequest req, AppDbContext db) =>
        {
            var name = (req.Name ?? "").Trim().ToLower();
            var ballot = await db.Ballots.Include(b => b.Points).AsNoTracking()
                .Where(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name)
                .OrderByDescending(b => b.SubmittedAt).FirstOrDefaultAsync();
            return ballot is null
                ? Results.NotFound()
                : Results.Ok(new { points = ballot.Points.ToDictionary(p => p.ActivityId, p => p.Points), unavailableSessions = ballot.UnavailableSessions });
        });

        // FairSlots this person is listed in (drafts stay hidden). Demo: the person is identified by name.
        api.MapPost("/my-slots", async (LookupRequest req, AppDbContext db) =>
        {
            var name = (req.Name ?? "").Trim().ToLower();
            return await db.FairSlots
                .Where(s => s.Phase != Phase.Draft && (s.Participants.Any(p => p.Name.ToLower() == name) || (s.OpenToAll && name != DemoData.NobodyName)))
                .OrderBy(s => s.Phase).ThenBy(s => s.RegistrationDeadline)
                .Select(s => new SlotSummaryDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline, s.Activities.Count, s.Ballots.Count))
                .ToListAsync();
        });

        // The own allocation once the organisers have published it.
        api.MapPost("/slots/{id:guid}/my-result", async (Guid id, LookupRequest req, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            var name = (req.Name ?? "").Trim();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (slot.Phase != Phase.Published) return Errors("notPublished");
            var result = await AdminEndpoints.AllocateAsync(db, slot);
            var mine = result.Participants.FirstOrDefault(p => !p.IsSample && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return Results.Ok(new { voted = mine is not null, assignments = mine?.Assignments ?? [] });
        });

        // Interim status while voting is open: what the person would get if the allocation ran now.
        api.MapPost("/slots/{id:guid}/my-status", async (Guid id, LookupRequest req, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            var name = (req.Name ?? "").Trim();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        // Which not-yet-fixed activities the person wants to keep following on the waiting list.
        api.MapPost("/slots/{id:guid}/waitlist", async (Guid id, WaitlistRequest req, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            var name = (req.Name ?? "").Trim();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (slot.Phase is not (Phase.Open or Phase.Closed)) return Errors("registrationClosed");
            var ballot = await db.Ballots.Include(b => b.Points)
                .Where(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name.ToLower())
                .OrderByDescending(b => b.SubmittedAt).FirstOrDefaultAsync();
            if (ballot is null) return Errors("notVoted");
            var pointed = ballot.Points.Where(p => p.Points > 0).Select(p => p.ActivityId).ToHashSet();
            ballot.Waitlist = (req.ActivityIds ?? []).Where(pointed.Contains).Distinct().ToList();
            await db.SaveChangesAsync();
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        api.MapGet("/slots/{id:guid}", async (Guid id, AppDbContext db, IConfiguration cfg) =>
            await ToDto(db, id, cfg) is { } dto ? Results.Ok(dto) : Results.NotFound());

        api.MapGet("/slots/{id:guid}/activities", async (Guid id, AppDbContext db) =>
            (await Database.LoadActivitiesAsync(db, id)).Select(ToDto));

        api.MapPost("/slots/{id:guid}/ballots", async (Guid id, BallotRequest req, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.Phase != Phase.Open) return Errors("registrationClosed");
            var name = (req.Name ?? "").Trim();
            if (!await Database.CanTakePartAsync(db, id, name)) return Errors("notListed");

            var activities = await Database.LoadActivitiesAsync(db, id);
            var points = (req.Points ?? []).Where(p => p.Value != 0).ToDictionary();
            var unavailable = (req.UnavailableSessions ?? []).ToHashSet();
            var errors = Validation.Check(new Ballot("new", name, points, unavailable), slot.ToRules(), activities);
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

            // Demo behaviour: the same name replaces its previous ballot. Step 2 replaces this with personal links.
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
        return new SlotDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline, s.ToRules(), ballots, s.SampleCount, !string.IsNullOrEmpty(cfg["Admin:Key"]), s.OpenToAll);
    }

    public static ActivityDto ToDto(Activity a) => new(a.Id, a.Title, a.RestrictedTo, a.Seats,
        a.Sessions.Select(s => new SessionDto(s.Id, s.Start, s.End, s.Location, s.Capacity, s.Flexible)).ToList(), a.GroupId, a.Description);

    /// <summary>
    /// Runs the allocation with the ballots so far. Per activity the person gave points to:
    /// "fixed" (would get it), "waitlist" (all seats taken, position = people without a seat who gave more points + 1)
    /// or "free" (seats left, but the person already has enough higher-ranked activities, or the date clashes).
    /// </summary>
    private static async Task<MyStatusDto> StatusAsync(AppDbContext db, FairSlotEntity slot, string name)
    {
        var activities = await Database.LoadActivitiesAsync(db, slot.Id);
        var entities = await Database.LoadBallotsAsync(db, slot.Id);
        var mine = entities.Where(b => !b.IsSample && b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).MaxBy(b => b.SubmittedAt);
        var ballots = entities.Select(b => b.ToModel()).ToList();
        var allocation = Allocator.PointBased(activities, ballots, slot.PerParticipant, slot.Seed, slot.ToRules().Groups);

        bool Full(Activity a) => a.Sessions.All(s => s.Capacity is { } c && allocation.Load(s) >= c);
        var full = activities.Where(Full).Select(a => a.Id).ToList();
        if (mine is null) return new MyStatusDto(false, [], full);

        var me = ballots.Single(b => b.Id == mine.Id.ToString());
        var assigned = allocation.Assignments[me.Id].ToDictionary(x => x.Session.ActivityId);
        var list = activities.Where(a => me.PointsFor(a.Id) > 0 || assigned.ContainsKey(a.Id))
            .OrderByDescending(a => me.PointsFor(a.Id))
            .Select(a =>
            {
                if (assigned.TryGetValue(a.Id, out var x))
                    return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), "fixed", null, false,
                        x.Session.Start, x.Session.End, x.Session.Flexible, x.Session.Location);
                var waiting = full.Contains(a.Id);
                int? position = waiting
                    ? 1 + ballots.Count(b => b.Id != me.Id && b.PointsFor(a.Id) > me.PointsFor(a.Id) && allocation.Assignments[b.Id].All(y => y.Session.ActivityId != a.Id))
                    : null;
                return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), waiting ? "waitlist" : "free", position,
                    mine.Waitlist.Contains(a.Id), null, null, false, null);
            }).ToList();
        return new MyStatusDto(true, list, full);
    }

    public static IResult Errors(string code) => Results.BadRequest(new ErrorsDto([new ValidationError(code)]));
}
