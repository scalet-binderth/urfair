using Microsoft.EntityFrameworkCore;
using UrFairSlot.Api.Data;
using UrFairSlot.Core;

namespace UrFairSlot.Api;

public record SessionDto(string Id, DateTime Start, DateTime? End, string Location, int? Capacity, bool Flexible);
public record ActivityDto(string Id, string Title, string? RestrictedTo, int? Seats, IReadOnlyList<SessionDto> Sessions);
public record ConfigDto(Rules Rules, DateTime? RegistrationDeadline, int BallotCount, int SampleCount, bool AdminProtected);
public record BallotRequest(string Name, Dictionary<string, int> Points, List<string>? UnavailableSessions);
public record ErrorsDto(IReadOnlyList<ValidationError> Errors);
public record DemandDto(string ActivityId, string Title, int? Seats, int Interested, int TotalPoints, double AveragePoints);
public record ResetRequest(int? SampleCount, bool NewSeed, bool RemoveRealBallots);
public record AssignmentDto(string ActivityId, string Title, string SessionId, DateTime Start, DateTime? End, bool Flexible, string Location, int Points, bool IsFallback);
public record ParticipantDto(string BallotId, string Name, bool IsSample, double Satisfaction, IReadOnlyList<AssignmentDto> Assignments);
public record AllocationDto(int Seed, int PerParticipant, Metrics PointBased, Metrics FirstComeFirstServed,
    IReadOnlyDictionary<string, int> SessionLoad, IReadOnlyList<ParticipantDto> Participants);

public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/config", async (AppDbContext db, IConfiguration cfg) =>
        {
            var s = await db.Settings.SingleAsync();
            return new ConfigDto(s.ToRules(), s.RegistrationDeadline, await db.Ballots.CountAsync(), s.SampleCount,
                !string.IsNullOrEmpty(cfg["Admin:Key"]));
        });

        api.MapGet("/activities", async (AppDbContext db) =>
            (await LoadActivities(db)).Select(a => new ActivityDto(a.Id, a.Title, a.RestrictedTo, a.Seats,
                a.Sessions.Select(s => new SessionDto(s.Id, s.Start, s.End, s.Location, s.Capacity, s.Flexible)).ToList())));

        api.MapPost("/ballots", async (BallotRequest req, AppDbContext db) =>
        {
            var settings = await db.Settings.SingleAsync();
            var activities = await LoadActivities(db);
            var name = (req.Name ?? "").Trim();
            var points = (req.Points ?? []).Where(p => p.Value != 0).ToDictionary();
            var unavailable = (req.UnavailableSessions ?? []).ToHashSet();
            var ballot = new Ballot("new", name, points, unavailable);

            var errors = Validation.Check(ballot, settings.ToRules(), activities);
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

            // Demo behaviour: the same name replaces its previous ballot. Later: personal links per participant.
            var existing = await db.Ballots.Where(b => !b.IsSample && b.Name.ToLower() == name.ToLower()).ToListAsync();
            db.Ballots.RemoveRange(existing);
            var entity = new BallotEntity
            {
                Name = name,
                Points = points.Select(p => new BallotPoints { ActivityId = p.Key, Points = p.Value }).ToList(),
                UnavailableSessions = unavailable.ToList(),
            };
            db.Ballots.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = entity.Id, replaced = existing.Count > 0 });
        });

        var admin = api.MapGroup("/admin").AddEndpointFilter(async (ctx, next) =>
        {
            // Optional protection: if Admin:Key is configured, the X-Admin-Key header must match.
            var key = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Admin:Key"];
            if (!string.IsNullOrEmpty(key) && ctx.HttpContext.Request.Headers["X-Admin-Key"] != key)
                return Results.Unauthorized();
            return await next(ctx);
        });

        admin.MapGet("/demand", async (AppDbContext db) =>
        {
            var activities = await LoadActivities(db);
            var ballots = await LoadBallots(db);
            return activities.Select(a =>
            {
                var interested = ballots.Where(b => b.PointsFor(a.Id) > 0).ToList();
                var total = interested.Sum(b => b.PointsFor(a.Id));
                return new DemandDto(a.Id, a.Title, a.Seats, interested.Count, total, interested.Count == 0 ? 0 : total / (double)interested.Count);
            }).OrderByDescending(d => d.TotalPoints).ToList();
        });

        admin.MapPut("/rules", async (Rules rules, AppDbContext db) =>
        {
            var r = rules with { Step = 5 };
            var errors = Validation.Check(r);
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));
            var s = await db.Settings.SingleAsync();
            (s.PointBudget, s.MinActivities, s.MaxPerActivity, s.PerParticipant) = (r.PointBudget, r.MinActivities, r.MaxPerActivity, r.PerParticipant);
            await db.SaveChangesAsync();
            await Database.ResetSamplesAsync(db); // sample ballots must match the new rules
            return Results.Ok(s.ToRules());
        });

        admin.MapPost("/reset-demo", async (ResetRequest req, AppDbContext db) =>
        {
            var s = await db.Settings.SingleAsync();
            if (req.SampleCount is { } n) s.SampleCount = Math.Clamp(n, 0, 1000);
            if (req.NewSeed) s.Seed = Random.Shared.Next(1000, 9999);
            await db.SaveChangesAsync();
            await Database.ResetSamplesAsync(db, req.RemoveRealBallots);
            return Results.Ok(new { s.SampleCount, s.Seed });
        });

        admin.MapGet("/allocation", async (AppDbContext db) =>
        {
            var s = await db.Settings.SingleAsync();
            var activities = await LoadActivities(db);
            var entities = await db.Ballots.Include(b => b.Points).AsNoTracking().ToListAsync();
            var ballots = entities.Select(b => b.ToModel()).ToList();
            var k = s.PerParticipant;

            var pointBased = Allocator.PointBased(activities, ballots, k, s.Seed);
            var fcfs = Allocator.FirstComeFirstServed(activities, ballots, k, s.Seed);
            var titles = activities.ToDictionary(a => a.Id, a => a.Title);
            var isSample = entities.ToDictionary(b => b.Id.ToString(), b => b.IsSample);

            var participants = ballots.Select(b => new ParticipantDto(b.Id, b.Name, isSample[b.Id], pointBased.Satisfaction(b, k),
                pointBased.Assignments[b.Id].OrderBy(x => x.Session.Start).Select(x => new AssignmentDto(
                    x.Session.ActivityId, titles[x.Session.ActivityId], x.Session.Id, x.Session.Start, x.Session.End,
                    x.Session.Flexible, x.Session.Location, x.Points, x.IsFallback)).ToList())).ToList();

            var load = activities.SelectMany(a => a.Sessions).ToDictionary(x => x.Id, pointBased.Load);
            return new AllocationDto(s.Seed, k, pointBased.Evaluate(k), fcfs.Evaluate(k), load, participants);
        });
    }

    private static async Task<List<Activity>> LoadActivities(AppDbContext db) =>
        (await db.Activities.Include(a => a.Sessions).AsNoTracking().OrderBy(a => a.SortOrder).ToListAsync())
        .Select(a => a.ToModel()).ToList();

    private static async Task<List<Ballot>> LoadBallots(AppDbContext db) =>
        (await db.Ballots.Include(b => b.Points).AsNoTracking().ToListAsync()).Select(b => b.ToModel()).ToList();
}
