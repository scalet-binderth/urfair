using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record SessionDto(string Id, DateTime Start, DateTime? End, string Location, int? Capacity, bool Flexible);
public record ActivityDto(string Id, string Title, string? RestrictedTo, int? Seats, IReadOnlyList<SessionDto> Sessions);
public record SlotSummaryDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, int ActivityCount, int BallotCount);
public record SlotDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, Rules Rules, int BallotCount, int SampleCount, bool AdminProtected, bool OpenToAll);
public record BallotRequest(string Name, Dictionary<string, int> Points, List<string>? UnavailableSessions);
public record ErrorsDto(IReadOnlyList<ValidationError> Errors);
public record LookupRequest(string Name);

/// <summary>Endpoints for participants.</summary>
public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Tells the start page whether the organisation area needs an access key.
        api.MapGet("/info", (IConfiguration cfg) => new { adminProtected = !string.IsNullOrEmpty(cfg["Admin:Key"]) });

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
            };
            db.Ballots.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = entity.Id, replaced = existing.Count > 0 });
        });
    }

    public static async Task<SlotDto?> ToDto(AppDbContext db, Guid id, IConfiguration cfg) =>
        await db.FairSlots.Where(s => s.Id == id)
            .Select(s => new SlotDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline,
                new Rules(s.PointBudget, s.MinActivities, s.MaxPerActivity, s.PerParticipant, 5),
                s.Ballots.Count, s.SampleCount, !string.IsNullOrEmpty(cfg["Admin:Key"]), s.OpenToAll))
            .SingleOrDefaultAsync();

    public static ActivityDto ToDto(Activity a) => new(a.Id, a.Title, a.RestrictedTo, a.Seats,
        a.Sessions.Select(s => new SessionDto(s.Id, s.Start, s.End, s.Location, s.Capacity, s.Flexible)).ToList());

    public static IResult Errors(string code) => Results.BadRequest(new ErrorsDto([new ValidationError(code)]));
}
