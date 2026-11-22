using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

/// <summary>What the signed-in account may do with one FairSlot.</summary>
/// <param name="CanEdit">Settings, activities, rules, sample data.</param>
/// <param name="Scope">Pfarrgemeinden whose participants may be seen and edited; null = all.</param>
public sealed record SlotAccess(FairSlotEntity Slot, bool CanEdit, IReadOnlySet<string>? Scope)
{
    public bool InScope(string? parish) => Scope is null || (parish is not null && Scope.Contains(parish));
}

/// <summary>
/// Permissions of organiser accounts, checked on the server for every request of the organisation area.
/// Junia admins may do everything. Pfarrgemeinde admins, by the FairSlot's visibility:
/// Pfarrgemeinde only (own Pfarrgemeinde): everything; Junia shared: everything, participants of their own Pfarrgemeinden only;
/// Junia read only: view, participants of their own Pfarrgemeinden only.
/// </summary>
public static class Access
{
    private const string AccountKey = "account", SlotKey = "slotAccess";

    public static AccountEntity Account(HttpContext http) => (AccountEntity)http.Items[AccountKey]!;
    public static SlotAccess Slot(HttpContext http) => (SlotAccess)http.Items[SlotKey]!;

    /// <summary>The signed-in account (from the cookie), loaded fresh so role changes apply immediately.</summary>
    public static async Task<AccountEntity?> CurrentAsync(HttpContext http, AppDbContext db)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    }

    public static bool CanSee(AccountEntity a, FairSlotEntity s) =>
        a.IsJuniaAdmin || s.Visibility != Visibility.ParishOnly || (s.Parish is not null && a.Parishes.Contains(s.Parish));

    public static SlotAccess For(AccountEntity a, FairSlotEntity s)
    {
        if (a.IsJuniaAdmin) return new(s, true, null);
        var own = s.Parish is not null && a.Parishes.Contains(s.Parish);
        return s.Visibility switch
        {
            Visibility.ParishOnly => new(s, own, own ? null : new HashSet<string>()),
            Visibility.JuniaShared => new(s, true, a.Parishes.ToHashSet()),
            _ => new(s, false, a.Parishes.ToHashSet()),
        };
    }

    /// <summary>
    /// Endpoint filter for /api/admin: requires an account and, for routes about one FairSlot (route value id,
    /// activityId or participantId), that the account may see it; changes outside the participant area need edit rights.
    /// </summary>
    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var account = await CurrentAsync(http, db);
        if (account is null) return Results.Unauthorized();
        http.Items[AccountKey] = account;

        var path = http.Request.Path.Value ?? "";
        if (path.StartsWith("/api/admin/accounts") || path == "/api/admin/reset-all")
            return account.IsJuniaAdmin ? await next(ctx) : Results.Forbid();

        var slotId = await SlotIdAsync(http, db);
        if (slotId is null) return await next(ctx); // e.g. the list of FairSlots or creating one
        var slot = await db.FairSlots.FindAsync(slotId.Value);
        if (slot is null || !CanSee(account, slot)) return Results.NotFound();

        var access = For(account, slot);
        http.Items[SlotKey] = access;
        var participantArea = path.Contains("/participants") || path.EndsWith("/invitations") || path.EndsWith("/reminders") || path.EndsWith("/mails");
        var change = !HttpMethods.IsGet(http.Request.Method);
        if (change && !participantArea && !access.CanEdit) return Results.Forbid();
        if (participantArea && access.Scope is { Count: 0 }) return Results.Forbid();
        // Ended is read only; only the settings (e.g. back to Closed) and deleting stay possible.
        if (change && slot.Phase == Phase.Ended && !HttpMethods.IsDelete(http.Request.Method)
            && !(HttpMethods.IsPut(http.Request.Method) && path.TrimEnd('/').Equals($"/api/admin/slots/{slot.Id}", StringComparison.OrdinalIgnoreCase)))
            return Results.BadRequest(new ErrorsDto([new ValidationError("slotEnded")]));
        return await next(ctx);
    }

    private static async Task<Guid?> SlotIdAsync(HttpContext http, AppDbContext db)
    {
        var values = http.Request.RouteValues;
        if (Guid.TryParse(values["id"]?.ToString(), out var id)) return id;
        if (values["activityId"]?.ToString() is { } activityId)
            return await db.Activities.Where(a => a.Id == activityId).Select(a => (Guid?)a.FairSlotId).FirstOrDefaultAsync() ?? Guid.Empty;
        if (Guid.TryParse(values["participantId"]?.ToString(), out var pid))
            return await db.Participants.Where(p => p.Id == pid).Select(p => (Guid?)p.FairSlotId).FirstOrDefaultAsync() ?? Guid.Empty;
        return null;
    }
}
