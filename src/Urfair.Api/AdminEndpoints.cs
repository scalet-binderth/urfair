using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record CreateSlotRequest(string Name, bool WithSampleData);
public record UpdateSlotRequest(string Name, Phase Phase, DateTime? RegistrationDeadline, Rules Rules, bool OpenToAll = false);
public record SessionRequest(string? Id, DateTime Start, DateTime? End, string? Location, int? Capacity, bool Flexible);
public record ActivityRequest(string Title, string? RestrictedTo, List<SessionRequest> Sessions);
public record DemandDto(string ActivityId, string Title, int? Seats, int Interested, int TotalPoints, double AveragePoints);
public record ParticipantListDto(Guid Id, string Name, bool HasVoted);
public record ResetRequest(int? SampleCount, bool NewSeed, bool RemoveRealBallots);
public record AssignmentDto(string ActivityId, string Title, string SessionId, DateTime Start, DateTime? End, bool Flexible, string Location, int Points, bool IsFallback);
public record ParticipantDto(string BallotId, string Name, bool IsSample, double Satisfaction, IReadOnlyList<AssignmentDto> Assignments);
public record AllocationDto(int Seed, int PerParticipant, Metrics PointBased, Metrics FirstComeFirstServed,
    IReadOnlyDictionary<string, int> SessionLoad, IReadOnlyList<ParticipantDto> Participants);

/// <summary>Endpoints for organisers. Optional protection via Admin:Key until a proper login exists.</summary>
public static class AdminEndpoints
{
    public static void MapAdminApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
        {
            var key = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Admin:Key"];
            if (!string.IsNullOrEmpty(key) && ctx.HttpContext.Request.Headers["X-Admin-Key"] != key)
                return Results.Unauthorized();
            return await next(ctx);
        });

        // ---------- FairSlots ----------

        admin.MapGet("/slots", async (AppDbContext db) =>
            await db.FairSlots.OrderByDescending(s => s.CreatedAt)
                .Select(s => new SlotSummaryDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline, s.Activities.Count, s.Ballots.Count))
                .ToListAsync());

        admin.MapPost("/slots", async (CreateSlotRequest req, AppDbContext db) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Endpoints.Errors("nameRequired");
            if (req.WithSampleData) return Results.Ok(new { (await Database.CreateDemoSlotAsync(db, name, Phase.Draft, 140)).Id });
            var slot = new FairSlotEntity { Name = name };
            db.FairSlots.Add(slot);
            await db.SaveChangesAsync();
            return Results.Ok(new { slot.Id });
        });

        admin.MapGet("/slots/{id:guid}", async (Guid id, AppDbContext db, IConfiguration cfg) =>
            await Endpoints.ToDto(db, id, cfg) is { } dto ? Results.Ok(dto) : Results.NotFound());

        admin.MapPut("/slots/{id:guid}", async (Guid id, UpdateSlotRequest req, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var name = (req.Name ?? "").Trim();
            var rules = req.Rules with { Step = 5 };
            var errors = Validation.Check(rules);
            if (name.Length == 0) errors.Insert(0, new ValidationError("nameRequired"));
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

            var rulesChanged = slot.ToRules() != rules;
            (slot.Name, slot.Phase, slot.RegistrationDeadline, slot.OpenToAll) = (name, req.Phase, req.RegistrationDeadline, req.OpenToAll);
            slot.Apply(rules);
            await db.SaveChangesAsync();
            if (rulesChanged) await Database.ResetSamplesAsync(db, id); // sample ballots must follow the new rules
            return Results.NoContent();
        });

        admin.MapDelete("/slots/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            db.FairSlots.Remove(slot);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---------- Activities and sessions ----------

        admin.MapGet("/slots/{id:guid}/activities", async (Guid id, AppDbContext db) =>
            (await Database.LoadActivitiesAsync(db, id)).Select(Endpoints.ToDto));

        admin.MapPost("/slots/{id:guid}/activities", async (Guid id, ActivityRequest req, AppDbContext db) =>
        {
            if (!await db.FairSlots.AnyAsync(s => s.Id == id)) return Results.NotFound();
            var entity = new ActivityEntity { FairSlotId = id, SortOrder = await db.Activities.CountAsync(a => a.FairSlotId == id) };
            if (Apply(entity, req) is { } error) return error;
            db.Activities.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(new { entity.Id });
        });

        admin.MapPut("/activities/{activityId}", async (string activityId, ActivityRequest req, AppDbContext db) =>
        {
            var entity = await db.Activities.Include(a => a.Sessions).SingleOrDefaultAsync(a => a.Id == activityId);
            if (entity is null) return Results.NotFound();
            if (Apply(entity, req) is { } error) return error;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapDelete("/activities/{activityId}", async (string activityId, AppDbContext db) =>
        {
            var entity = await db.Activities.SingleOrDefaultAsync(a => a.Id == activityId);
            if (entity is null) return Results.NotFound();
            // Remove the points ballots gave to this activity.
            foreach (var b in await db.Ballots.Include(b => b.Points).Where(b => b.FairSlotId == entity.FairSlotId).ToListAsync())
                b.Points.RemoveAll(p => p.ActivityId == activityId);
            db.Activities.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---------- Demand, demo data, allocation ----------

        admin.MapGet("/slots/{id:guid}/demand", async (Guid id, AppDbContext db) =>
        {
            var activities = await Database.LoadActivitiesAsync(db, id);
            var ballots = (await Database.LoadBallotsAsync(db, id)).Select(b => b.ToModel()).ToList();
            return activities.Select(a =>
            {
                var interested = ballots.Where(b => b.PointsFor(a.Id) > 0).ToList();
                var total = interested.Sum(b => b.PointsFor(a.Id));
                return new DemandDto(a.Id, a.Title, a.Seats, interested.Count, total, interested.Count == 0 ? 0 : total / (double)interested.Count);
            }).OrderByDescending(d => d.TotalPoints).ToList();
        });

        admin.MapPost("/slots/{id:guid}/reset-demo", async (Guid id, ResetRequest req, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (req.SampleCount is { } n) slot.SampleCount = Math.Clamp(n, 0, 1000);
            if (req.NewSeed) slot.Seed = Random.Shared.Next(1000, 9999);
            await db.SaveChangesAsync();
            await Database.ResetSamplesAsync(db, id, req.RemoveRealBallots);
            return Results.Ok(new { slot.SampleCount, slot.Seed });
        });

        admin.MapGet("/slots/{id:guid}/allocation", async (Guid id, AppDbContext db) =>
            await db.FairSlots.FindAsync(id) is { } slot ? Results.Ok(await AllocateAsync(db, slot)) : Results.NotFound());

        // ---------- Participant list ----------

        admin.MapGet("/slots/{id:guid}/participants", async (Guid id, AppDbContext db) =>
        {
            var voted = await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample).Select(b => b.Name.ToLower()).ToListAsync();
            return (await db.Participants.Where(p => p.FairSlotId == id).OrderBy(p => p.Name).ToListAsync())
                .Select(p => new ParticipantListDto(p.Id, p.Name, voted.Contains(p.Name.ToLower())));
        });

        admin.MapPost("/slots/{id:guid}/participants", async (Guid id, LookupRequest req, AppDbContext db) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Endpoints.Errors("nameRequired");
            if (!await db.FairSlots.AnyAsync(s => s.Id == id)) return Results.NotFound();
            if (await Database.IsParticipantAsync(db, id, name)) return Endpoints.Errors("alreadyListed");
            db.Participants.Add(new ParticipantEntity { FairSlotId = id, Name = name });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapDelete("/participants/{participantId:guid}", async (Guid participantId, AppDbContext db) =>
        {
            var p = await db.Participants.FindAsync(participantId);
            if (p is null) return Results.NotFound();
            db.Participants.Remove(p);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Runs both allocation procedures for a FairSlot and shapes the result for the UI.</summary>
    public static async Task<AllocationDto> AllocateAsync(AppDbContext db, FairSlotEntity slot)
    {
        var activities = await Database.LoadActivitiesAsync(db, slot.Id);
        var entities = await Database.LoadBallotsAsync(db, slot.Id);
        var ballots = entities.Select(b => b.ToModel()).ToList();
        var k = slot.PerParticipant;

        var pointBased = Allocator.PointBased(activities, ballots, k, slot.Seed);
        var fcfs = Allocator.FirstComeFirstServed(activities, ballots, k, slot.Seed);
        var titles = activities.ToDictionary(a => a.Id, a => a.Title);
        var isSample = entities.ToDictionary(b => b.Id.ToString(), b => b.IsSample);

        var participants = ballots.Select(b => new ParticipantDto(b.Id, b.Name, isSample[b.Id], pointBased.Satisfaction(b, k),
            pointBased.Assignments[b.Id].OrderBy(x => x.Session.Start).Select(x => new AssignmentDto(
                x.Session.ActivityId, titles[x.Session.ActivityId], x.Session.Id, x.Session.Start, x.Session.End,
                x.Session.Flexible, x.Session.Location, x.Points, x.IsFallback)).ToList())).ToList();

        var load = activities.SelectMany(a => a.Sessions).ToDictionary(x => x.Id, pointBased.Load);
        return new AllocationDto(slot.Seed, k, pointBased.Evaluate(k), fcfs.Evaluate(k), load, participants);
    }

    /// <summary>Copies a request onto an activity entity (sessions are matched by id). Returns an error result if invalid.</summary>
    private static IResult? Apply(ActivityEntity entity, ActivityRequest req)
    {
        var restricted = string.IsNullOrWhiteSpace(req.RestrictedTo) ? null : req.RestrictedTo;
        var sessions = (req.Sessions ?? []).Select(s =>
            new Session(s.Id ?? "", entity.Id, s.Start, s.Flexible ? null : s.End, (s.Location ?? "").Trim(), s.Capacity, s.Flexible)).ToList();
        var errors = Validation.Check(new Activity(entity.Id, (req.Title ?? "").Trim(), sessions, restricted));
        if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

        entity.Title = req.Title!.Trim();
        entity.RestrictedTo = restricted;
        var keep = new List<SessionEntity>();
        foreach (var s in sessions)
        {
            var target = entity.Sessions.FirstOrDefault(x => x.Id == s.Id) ?? new SessionEntity { ActivityId = entity.Id };
            (target.Start, target.End, target.Location, target.Capacity, target.Flexible) = (s.Start, s.End, s.Location, s.Capacity, s.Flexible);
            keep.Add(target);
        }
        entity.Sessions.RemoveAll(x => !keep.Contains(x));
        foreach (var s in keep.Where(s => !entity.Sessions.Contains(s))) entity.Sessions.Add(s);
        return null;
    }
}
