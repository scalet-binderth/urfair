using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record LeaderActivityDto(string Id, string Title);
/// <param name="IsOrganiser">The vote's organiser (e.g. the Pfarrgemeinde): leads every activity without a leader of its own.</param>
/// <param name="Emails">More than one address among the leader's activities (older data); saving sets one for all.</param>
public record LeaderDto(string? Name, string? Email, bool IsOrganiser, IReadOnlyList<LeaderActivityDto> Activities, IReadOnlyList<string> Emails);
public record LeaderUpdateRequest(string? OldName, bool IsOrganiser, string? Name, string? Email);

/// <summary>
/// The leaders of a vote's activities. A leader is known by name; one leader has one e-mail address for all their activities.
/// Activities without a leader are led by the organiser, whose address is the one in the settings. Mails to leaders (lists,
/// changes) go out only when the organisers decide.
/// </summary>
public static class Leaders
{
    static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the organiser leads the activity (no leader, or the organiser named as leader).</summary>
    public static bool ByOrganiser(string? leader, FairSlotEntity slot) => leader is null || slot.Organiser is not null && Same(leader, slot.Organiser);

    /// <summary>Where mails for an activity's leader go: their own address, otherwise the organiser's if the organiser leads it.</summary>
    public static string? EmailOf(string? leader, string? leaderEmail, FairSlotEntity slot) =>
        leaderEmail ?? (ByOrganiser(leader, slot) ? slot.OrganiserEmail : null);
    public static string? EmailOf(ActivityEntity a, FairSlotEntity slot) => EmailOf(a.Leader, a.LeaderEmail, slot);
    public static string? NameOf(ActivityEntity a, FairSlotEntity slot) => a.Leader ?? slot.Organiser;

    /// <summary>A leader needs an address (unless the organiser leads it), an address needs a leader.</summary>
    public static List<ValidationError> Check(string? leader, string? leaderEmail, FairSlotEntity slot) =>
        leader is null && leaderEmail is not null ? [new("leaderRequired")]
        : EmailOf(leader, leaderEmail, slot) is null ? [new(ByOrganiser(leader, slot) ? "organiserEmailRequired" : "leaderEmailRequired")]
        : [];

    /// <summary>One leader has one address: the other activities of the same leader get it too.</summary>
    public static async Task ShareEmailAsync(AppDbContext db, ActivityEntity changed)
    {
        if (changed.Leader is null || changed.LeaderEmail is null) return;
        foreach (var a in await db.Activities.Where(a => a.FairSlotId == changed.FairSlotId && a.Id != changed.Id && a.Leader != null).ToListAsync())
            if (Same(a.Leader, changed.Leader)) a.LeaderEmail = changed.LeaderEmail;
    }

    public static void MapLeaders(this RouteGroupBuilder admin)
    {
        admin.MapGet("/slots/{id:guid}/leaders", async (Guid id, AppDbContext db) =>
            await db.FairSlots.FindAsync(id) is { } slot ? Results.Ok(await ListAsync(db, slot)) : Results.NotFound());

        // Name and address of one leader, for all their activities. For the organiser also in the settings.
        admin.MapPut("/slots/{id:guid}/leaders", async (Guid id, LeaderUpdateRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var name = Validation.Clean(req.Name);
            var email = Validation.Clean(req.Email);
            var errors = new List<ValidationError>();
            if (name is null && !req.IsOrganiser) errors.Add(new ValidationError("leaderRequired"));
            if (name is { Length: > Validation.MaxShortText })
                errors.Add(new ValidationError("textTooLong", new Dictionary<string, object> { ["max"] = Validation.MaxShortText }));
            if (email is null) errors.Add(new ValidationError(req.IsOrganiser ? "organiserEmailRequired" : "leaderEmailRequired"));
            else if (!Validation.IsEmail(email)) errors.Add(new ValidationError("emailInvalid"));
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

            var activities = await db.Activities.Where(a => a.FairSlotId == id && a.Leader != null).ToListAsync();
            var oldName = req.IsOrganiser ? slot.Organiser : Validation.Clean(req.OldName);
            var before = $"{oldName} <{(req.IsOrganiser ? slot.OrganiserEmail : activities.FirstOrDefault(a => Same(a.Leader, oldName))?.LeaderEmail)}>";
            foreach (var a in activities.Where(a => oldName is not null && Same(a.Leader, oldName)))
                (a.Leader, a.LeaderEmail) = req.IsOrganiser && name is null ? (null, null) : (name, email);
            if (req.IsOrganiser) (slot.Organiser, slot.OrganiserEmail) = (name, email);
            var after = $"{name} <{email}>";
            if (before != after) await AllocationEndpoints.LogChangeAsync(db, http, id, "leader", name ?? oldName, before, after);
            await db.SaveChangesAsync();
            return Results.Ok(await ListAsync(db, slot));
        });

        // Leaders who do not know about changes to their lists yet, and mailing them (all, or the chosen addresses).
        admin.MapGet("/slots/{id:guid}/leader-changes", async (Guid id, AppDbContext db) =>
            await db.FairSlots.FindAsync(id) is { } slot ? Results.Ok(await LeaderNotices.ChangesAsync(db, slot)) : Results.NotFound());
        admin.MapPost("/slots/{id:guid}/leader-changes/send", async (Guid id, LeaderChangesRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.FrozenAt is null) return Endpoints.Errors("notFrozen");
            if (Access.Slot(http).Scope is not null) return Endpoints.Errors("leaderMailsNeedAll");
            return Results.Ok(new { created = await LeaderNotices.SendAsync(db, slot, req.Emails is { Count: > 0 } e ? e : null) });
        });
    }

    /// <summary>The organiser first (also without activities), then the other leaders by name.</summary>
    public static async Task<List<LeaderDto>> ListAsync(AppDbContext db, FairSlotEntity slot)
    {
        var activities = await db.Activities.AsNoTracking().Where(a => a.FairSlotId == slot.Id).OrderBy(a => a.SortOrder).ToListAsync();
        LeaderActivityDto Dto(ActivityEntity a) => new(a.Id, a.Title);
        var own = activities.Where(a => ByOrganiser(a.Leader, slot)).ToList();
        var organiser = new LeaderDto(slot.Organiser, slot.OrganiserEmail, true, own.Select(Dto).ToList(),
            Emails(own.Select(a => a.LeaderEmail).Append(slot.OrganiserEmail)));
        var others = activities.Where(a => !ByOrganiser(a.Leader, slot))
            .GroupBy(a => a.Leader!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new LeaderDto(g.First().Leader!.Trim(), g.Select(a => a.LeaderEmail).FirstOrDefault(e => e is not null), false,
                g.Select(Dto).ToList(), Emails(g.Select(a => a.LeaderEmail))))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase);
        return [organiser, .. others];
    }

    /// <summary>The distinct addresses, if there is more than one.</summary>
    static List<string> Emails(IEnumerable<string?> emails)
    {
        var distinct = emails.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count > 1 ? distinct : [];
    }

    /// <summary>
    /// Imported activities with one address per leader: the one entered in the import, else the CSV's, else the one the vote
    /// already knows for that name. Also who leads them; a <c>null</c> address is still missing.
    /// </summary>
    public static async Task<(List<Activity> Activities, List<LeaderDto> Leaders)> ForImportAsync(AppDbContext db, FairSlotEntity slot,
        IReadOnlyList<Activity> parsed, IReadOnlyDictionary<string, string>? entered, string? organiserEmail)
    {
        var given = (entered ?? new Dictionary<string, string>())
            .Where(x => Validation.Clean(x.Key) is not null && Validation.Clean(x.Value) is not null)
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value.Trim(), StringComparer.OrdinalIgnoreCase);
        var known = (await db.Activities.AsNoTracking().Where(a => a.FairSlotId == slot.Id && a.Leader != null && a.LeaderEmail != null).ToListAsync())
            .GroupBy(a => a.Leader!.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().LeaderEmail!, StringComparer.OrdinalIgnoreCase);
        var emails = parsed.Where(a => !ByOrganiser(a.Leader, slot)).GroupBy(a => a.Leader!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => given.GetValueOrDefault(g.Key) ?? g.Select(a => a.LeaderEmail).FirstOrDefault(e => e is not null) ?? known.GetValueOrDefault(g.Key),
                StringComparer.OrdinalIgnoreCase);
        var activities = parsed.Select(a => ByOrganiser(a.Leader, slot) ? a : a with { Leader = a.Leader!.Trim(), LeaderEmail = emails[a.Leader!.Trim()] }).ToList();

        LeaderActivityDto Dto(Activity a) => new(a.Id, a.Title);
        var leaders = new List<LeaderDto>();
        var own = activities.Where(a => ByOrganiser(a.Leader, slot) && a.LeaderEmail is null).ToList();
        if (own.Count > 0) leaders.Add(new LeaderDto(slot.Organiser, organiserEmail ?? slot.OrganiserEmail, true, own.Select(Dto).ToList(), []));
        leaders.AddRange(activities.Where(a => !ByOrganiser(a.Leader, slot)).GroupBy(a => a.Leader!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LeaderDto(g.Key, g.First().LeaderEmail, false, g.Select(Dto).ToList(), []))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase));
        return (activities, leaders);
    }

}
