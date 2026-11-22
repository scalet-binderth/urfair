using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record CreateSlotRequest(string Name, bool WithSampleData);
public record AdminSlotSummaryDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, int ActivityCount, int BallotCount,
    string? Parish, Visibility Visibility, bool CanEdit);
public record UpdateSlotRequest(string Name, Phase Phase, DateTime? RegistrationDeadline, Rules Rules, bool OpenToAll = false, string? Parish = null,
    string? Organiser = null, Visibility Visibility = Visibility.JuniaShared, bool ShowInterim = false);
public record SessionRequest(string? Id, DateTime Start, DateTime? End, string? Location, int? Capacity, bool Flexible);
public record ActivityRequest(string Title, string? RestrictedTo, List<SessionRequest> Sessions, string? GroupId = null, string? Description = null, string? Parish = null, string? Leader = null, string? LeaderEmail = null);
public record DemandDto(string ActivityId, string Title, int? Seats, int Interested, int TotalPoints, double AveragePoints);
public record ResetRequest(int? SampleCount, bool NewSeed, bool RemoveRealBallots);
public record ImportRequest(string? Csv, bool Replace, bool Commit);
public record ImportPreviewDto(IReadOnlyList<ActivityDto> Activities, IReadOnlyList<ValidationError> Errors, bool Saved);

/// <summary>Endpoints for organisers (signed-in accounts; permissions in Access.cs).</summary>
public static class AdminEndpoints
{
    public static void MapAdminApi(this WebApplication app)
    {
        // Every request needs a signed-in account; the filter also checks access to the FairSlot (Access.cs).
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(Access.Filter);
        admin.MapAccountAdmin();

        // ---------- FairSlots ----------

        // FairSlots the account may see.
        admin.MapGet("/slots", async (AppDbContext db, HttpContext http) =>
        {
            var account = Access.Account(http);
            var slots = await db.FairSlots.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync();
            var visible = slots.Where(s => Access.CanSee(account, s)).Select(s => s.Id).ToList();
            var counts = await db.FairSlots.Where(s => visible.Contains(s.Id))
                .Select(s => new { s.Id, Activities = s.Activities.Count, Ballots = s.Ballots.Count }).ToDictionaryAsync(x => x.Id);
            return slots.Where(s => visible.Contains(s.Id)).Select(s => new AdminSlotSummaryDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline,
                counts[s.Id].Activities, counts[s.Id].Ballots, s.Parish, s.Visibility, Access.For(account, s).CanEdit));
        });

        // A Pfarrgemeinde admin creates FairSlots for their own (first) Pfarrgemeinde, visible only there; Junia admins for all.
        admin.MapPost("/slots", async (CreateSlotRequest req, AppDbContext db, IConfiguration cfg, HttpContext http) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Endpoints.Errors("nameRequired");
            var account = Access.Account(http);
            var slot = req.WithSampleData && Database.IsDemo(cfg)
                ? await Database.CreateDemoSlotAsync(db, name, Phase.Draft, 140)
                : new FairSlotEntity { Name = name };
            if (!account.IsJuniaAdmin) (slot.Parish, slot.Visibility) = (account.Parishes.FirstOrDefault(), Visibility.ParishOnly);
            if (db.Entry(slot).State == EntityState.Detached) db.FairSlots.Add(slot);
            await db.SaveChangesAsync();
            return Results.Ok(new { slot.Id });
        });

        admin.MapGet("/slots/{id:guid}", async (Guid id, AppDbContext db, IConfiguration cfg, HttpContext http) =>
            await Endpoints.ToDto(db, id, cfg) is { } dto ? Results.Ok(dto with { CanEdit = Access.Slot(http).CanEdit }) : Results.NotFound());

        admin.MapPut("/slots/{id:guid}", async (Guid id, UpdateSlotRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var name = (req.Name ?? "").Trim();
            // New groups get an id; colours fall back to green.
            var groups = (req.Rules.Groups ?? []).Select(g => g with
            {
                Id = string.IsNullOrWhiteSpace(g.Id) ? Ids.New() : g.Id.Trim(), Name = (g.Name ?? "").Trim(),
                Color = string.IsNullOrWhiteSpace(g.Color) ? "green" : g.Color,
            }).ToList();
            var rules = req.Rules with { Step = 5, Groups = groups };
            var errors = Validation.Check(rules);
            if (name.Length == 0) errors.Insert(0, new ValidationError("nameRequired"));
            if (ParishOf(req.Parish) is { } parish && !Parishes.All.Contains(parish))
                errors.Add(new ValidationError("parishUnknown", new Dictionary<string, object> { ["value"] = parish }));
            // Pfarrgemeinde admins may move a FairSlot only to their own Pfarrgemeinde and never make it read-only for themselves.
            var account = Access.Account(http);
            if (!account.IsJuniaAdmin && (ParishOf(req.Parish) != slot.Parish || req.Visibility != slot.Visibility) &&
                (req.Visibility == Visibility.JuniaReadOnly || ParishOf(req.Parish) is not { } p || !account.Parishes.Contains(p)))
                errors.Add(new ValidationError("visibilityNotAllowed"));
            if (Clean(req.Organiser) is { Length: > Validation.MaxShortText })
                errors.Add(new ValidationError("textTooLong", new Dictionary<string, object> { ["max"] = Validation.MaxShortText }));
            // A frozen allocation must stay valid: no other rules. Back to registration only from the internal allocation
            // (it is unfrozen then); once participants have seen it, it stays.
            var rulesChanged = slot.ToRules() != rules;
            var reopen = req.Phase is Phase.Draft or Phase.Open && Phases.HasAllocation(slot.Phase);
            if (slot.FrozenAt is not null && rulesChanged && !reopen)
                errors.Add(new ValidationError("allocationFrozen"));
            if (reopen && slot.Phase != Phase.Allocation) errors.Add(new ValidationError("unpublishFirst"));
            if (req.Phase == Phase.Open && req.RegistrationDeadline is { } deadline && deadline <= Phases.Now())
                errors.Add(new ValidationError("deadlinePassed"));
            // It would end again at once.
            if (slot.Phase == Phase.Ended && req.Phase != Phase.Ended && await Phases.LastDayPassedAsync(db, id))
                errors.Add(new ValidationError("endedFinal"));
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

            var before = slot.Phase;
            (slot.Name, slot.Phase, slot.RegistrationDeadline, slot.OpenToAll, slot.ShowInterim) = (name, req.Phase, req.RegistrationDeadline, req.OpenToAll, req.ShowInterim);
            (slot.Parish, slot.Organiser, slot.Visibility) = (ParishOf(req.Parish), Clean(req.Organiser), req.Visibility);
            slot.Apply(rules);
            // Activities of a removed group become ungrouped.
            var ids = groups.Select(g => g.Id).ToHashSet();
            foreach (var a in await db.Activities.Where(a => a.FairSlotId == id && a.GroupId != null).ToListAsync())
                if (!ids.Contains(a.GroupId!)) a.GroupId = null;
            if (before != slot.Phase) AllocationEndpoints.Log(db, http, id, "phase", null, before.ToString(), slot.Phase.ToString());
            await db.SaveChangesAsync();
            if (reopen)
            {
                AllocationEndpoints.Log(db, http, id, "unfrozen");
                await AllocationEndpoints.UnfreezeAsync(db, slot);
            }
            if (rulesChanged) await Database.ResetSamplesAsync(db, id); // sample ballots must follow the new rules
            // From the allocation on, the stored result counts.
            if (Phases.HasAllocation(slot.Phase) && slot.FrozenAt is null)
            {
                AllocationEndpoints.Log(db, http, id, "frozen");
                await AllocationEndpoints.FreezeAsync(db, slot);
            }
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
            (await Database.LoadActivitiesAsync(db, id)).Select(Endpoints.ToAdminDto));

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

        // ---------- Excel/CSV import and export ----------

        // The current activities as CSV (also the template: edit in Excel, import again).
        admin.MapGet("/slots/{id:guid}/activities.csv", async (Guid id, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var csv = ActivityCsv.Write(await Database.LoadActivitiesAsync(db, id), slot.ToRules().GroupList);
            // BOM so that Excel recognises UTF-8 (umlauts).
            return Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv; charset=utf-8", "veranstaltungen.csv");
        });

        // Preview (commit=false) or take over (commit=true) activities from CSV text.
        admin.MapPost("/slots/{id:guid}/import", async (Guid id, ImportRequest req, AppDbContext db) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var result = ActivityCsv.Parse(req.Csv ?? "", slot.ToRules().GroupList);
            if (!req.Commit || result.Errors.Count > 0 || result.Activities.Count == 0)
                return Results.Ok(new ImportPreviewDto(result.Activities.Select(Endpoints.ToAdminDto).ToList(), result.Errors, false));
            if (req.Replace && slot.FrozenAt is not null) return Endpoints.Errors("allocationFrozen");

            var order = await db.Activities.CountAsync(a => a.FairSlotId == id);
            if (req.Replace)
            {
                // Replacing removes the old activities and the points ballots gave to them.
                db.Activities.RemoveRange(await db.Activities.Where(a => a.FairSlotId == id).ToListAsync());
                foreach (var b in await db.Ballots.Include(b => b.Points).Where(b => b.FairSlotId == id).ToListAsync()) b.Points.Clear();
                order = 0;
            }
            foreach (var a in result.Activities) db.Activities.Add(Database.ToEntity(a, id, order++));
            await db.SaveChangesAsync();
            if (req.Replace) await Database.ResetSamplesAsync(db, id); // sample ballots must refer to the new activities
            return Results.Ok(new ImportPreviewDto(result.Activities.Select(Endpoints.ToAdminDto).ToList(), [], true));
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

        // Demo only: rebuild every demo FairSlot (since migrations, deploys keep the data).
        admin.MapPost("/reset-all", async (AppDbContext db, IConfiguration cfg) =>
        {
            if (!Database.IsDemo(cfg)) return Results.NotFound();
            await Database.ResetDemoAsync(db);
            return Results.NoContent();
        });

        admin.MapPost("/slots/{id:guid}/reset-demo", async (Guid id, ResetRequest req, AppDbContext db, IConfiguration cfg) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null || !Database.IsDemo(cfg)) return Results.NotFound();
            if (req.SampleCount is { } n) slot.SampleCount = Math.Clamp(n, 0, 1000);
            if (req.NewSeed) slot.Seed = Random.Shared.Next(1000, 9999);
            await db.SaveChangesAsync();
            await Database.ResetSamplesAsync(db, id, req.RemoveRealBallots);
            return Results.Ok(new { slot.SampleCount, slot.Seed });
        });

        // ---------- Allocation: live, frozen, moved by hand (AllocationEndpoints.cs) ----------
        admin.MapAllocationAdmin();

        // ---------- Participant list, import, invitations (ParticipantEndpoints.cs) ----------
        admin.MapParticipantAdmin();
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Official parish name for a full or short form; unknown text is kept so validation can report it.</summary>
    private static string? ParishOf(string? text) => Parishes.Match(text) ?? Clean(text);

    /// <summary>Copies a request onto an activity entity (sessions are matched by id). Returns an error result if invalid.</summary>
    private static IResult? Apply(ActivityEntity entity, ActivityRequest req)
    {
        var restricted = string.IsNullOrWhiteSpace(req.RestrictedTo) ? null : req.RestrictedTo;
        var sessions = (req.Sessions ?? []).Select(s =>
            new Session(s.Id ?? "", entity.Id, s.Start, s.Flexible ? null : s.End, (s.Location ?? "").Trim(), s.Capacity, s.Flexible)).ToList();
        var errors = Validation.Check(new Activity(entity.Id, (req.Title ?? "").Trim(), sessions, restricted, Description: req.Description?.Trim(), Parish: ParishOf(req.Parish), Leader: Clean(req.Leader), LeaderEmail: Clean(req.LeaderEmail)));
        if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

        entity.Title = req.Title!.Trim();
        entity.RestrictedTo = restricted;
        entity.GroupId = string.IsNullOrWhiteSpace(req.GroupId) ? null : req.GroupId;
        entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        entity.Parish = ParishOf(req.Parish);
        entity.Leader = Clean(req.Leader);
        entity.LeaderEmail = Clean(req.LeaderEmail);
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
