using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;

namespace Urfair.Api;

/// <summary>
/// Phase rules that depend on the time. Dates of deadlines and sessions are wall-clock times in Austria
/// (stored without a time zone), so "now" is Vienna time as well.
/// </summary>
public static class Phases
{
    /// <summary>Who changed the phase in the log when nobody did it by hand.</summary>
    public const string Clock = "Urfair";

    private static readonly TimeZoneInfo Zone = FindZone();

    public static DateTime Now() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Phases with a frozen allocation.</summary>
    public static bool HasAllocation(Phase p) => p is Phase.Allocation or Phase.Published or Phase.Closed or Phase.Ended;

    /// <summary>Participants see the frozen allocation (their seats and waiting-list positions).</summary>
    public static bool ShowsResult(Phase p) => p is Phase.Published or Phase.Closed or Phase.Ended;

    public static bool DeadlinePassed(FairSlotEntity s) => s.RegistrationDeadline is { } d && d <= Now();

    /// <summary>The day of the last session (end, otherwise start), or null without sessions.</summary>
    public static async Task<DateOnly?> LastDayAsync(AppDbContext db, Guid slotId)
    {
        var times = await db.Activities.Where(a => a.FairSlotId == slotId).SelectMany(a => a.Sessions).Select(s => s.End ?? s.Start).ToListAsync();
        return times.Count == 0 ? null : DateOnly.FromDateTime(times.Max());
    }

    public static async Task<bool> LastDayPassedAsync(AppDbContext db, Guid slotId) =>
        await LastDayAsync(db, slotId) is { } last && last < DateOnly.FromDateTime(Now());

    /// <summary>
    /// Registration closes at the deadline: the allocation is calculated and frozen (Allocation, internal).
    /// After the last date an allocation phase becomes Ended. Allocation phases without a frozen allocation (from before
    /// the phases were tied to freezing) are frozen. Free seats of visible allocations move up (e.g. after a participant was deleted).
    /// Seat offers that expired go to the next person (<see cref="SeatOffers"/>). Ended votes are deleted after the retention period
    /// by <see cref="PhaseClock"/> (<see cref="Retention"/>).
    /// </summary>
    public static async Task TickAsync(AppDbContext db)
    {
        var now = Now();
        foreach (var slot in await db.FairSlots.Where(s => s.Phase == Phase.Open && s.RegistrationDeadline != null && s.RegistrationDeadline <= now).ToListAsync())
        {
            slot.Phase = Phase.Allocation;
            Log(db, slot.Id, "phase", nameof(Phase.Open), nameof(Phase.Allocation));
            Log(db, slot.Id, "frozen");
            await AllocationEndpoints.FreezeAsync(db, slot);
        }

        var allocating = new[] { Phase.Allocation, Phase.Published, Phase.Closed };
        foreach (var slot in await db.FairSlots.Where(s => allocating.Contains(s.Phase)).ToListAsync())
        {
            if (await LastDayPassedAsync(db, slot.Id))
            {
                Log(db, slot.Id, "phase", slot.Phase.ToString(), nameof(Phase.Ended));
                slot.Phase = Phase.Ended;
                slot.EndedAt = now;
            }
            if (slot.FrozenAt is null)
            {
                Log(db, slot.Id, "frozen");
                await AllocationEndpoints.FreezeAsync(db, slot);
            }
            await MovingUp.FillAsync(db, slot);
        }
        // Ended before the deletion period was stored: it starts now.
        foreach (var slot in await db.FairSlots.Where(s => s.Phase == Phase.Ended && s.EndedAt == null).ToListAsync())
            slot.EndedAt = now;
        await db.SaveChangesAsync();
        await SeatOffers.TickAsync(db);
    }

    private static void Log(AppDbContext db, Guid slotId, string kind, string? before = null, string? after = null) =>
        AllocationEndpoints.Log(db, slotId, Clock, kind, null, before, after);

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna"); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Local; }
    }
}

/// <summary>Checks the time-driven phase changes and the automatic deletion once a minute (and right after the start).</summary>
public sealed class PhaseClock(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<PhaseClock> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await Phases.TickAsync(db);
                var days = Retention.Days(cfg);
                foreach (var name in await Retention.DeleteExpiredAsync(db, days))
                    logger.LogInformation("Deleted the vote \"{Name}\" with all its data, {Days} days after it ended.", name, days);
                await Retention.DeleteOldAccountMailsAsync(db);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Phase check failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
