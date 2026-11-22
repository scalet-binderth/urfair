using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record ParticipantListDto(Guid Id, string Name, string? Email, DateOnly? BirthDate, string? Parish, string Token, DateTime? InvitedAt, bool HasVoted);
public record ParticipantRequest(string? Name, string? Email, DateOnly? BirthDate, string? Parish);
public record ParticipantImportRequest(string? Csv, bool Commit);
public record ParticipantImportDto(IReadOnlyList<ParticipantCsv.Person> People, IReadOnlyList<ValidationError> Errors, int New, int Updated, bool Saved);
public record MailDto(Guid Id, string To, string Subject, string Body, DateTime CreatedAt);
public record LinkInfoDto(Guid SlotId, string SlotName, bool NeedsCheck);
public record LinkCheckRequest(DateOnly? BirthDate);

/// <summary>
/// Participant list (by hand and via CSV), invitations with personal links, and the sign-in via such a link.
/// A link is /p/{token}; if a date of birth is stored, it must be confirmed. Afterwards the browser sends
/// token and date with every participant request (headers X-Participant, X-Participant-Check), so nothing is kept on the server.
/// </summary>
public static class ParticipantEndpoints
{
    private const int MaxFailedChecks = 10;

    public static void MapParticipantAdmin(this RouteGroupBuilder admin)
    {
        admin.MapGet("/slots/{id:guid}/participants", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            var voted = await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample).Select(b => b.Name.ToLower()).ToListAsync();
            var access = Access.Slot(http);
            return (await db.Participants.Where(p => p.FairSlotId == id).OrderBy(p => p.Parish).ThenBy(p => p.Name).ToListAsync())
                .Where(p => access.InScope(p.Parish))
                .Select(p => new ParticipantListDto(p.Id, p.Name, p.Email, p.BirthDate, p.Parish, p.Token, p.InvitedAt, voted.Contains(p.Name.ToLower())));
        });

        admin.MapPost("/slots/{id:guid}/participants", async (Guid id, ParticipantRequest req, AppDbContext db, HttpContext http) =>
        {
            if (!await db.FairSlots.AnyAsync(s => s.Id == id)) return Results.NotFound();
            if (Check(req, out var person) is { } error) return error;
            if (!Access.Slot(http).InScope(person.Parish)) return Endpoints.Errors("parishNotOwn");
            if (await Database.IsParticipantAsync(db, id, person.Name)) return Endpoints.Errors("alreadyListed");
            db.Participants.Add(new ParticipantEntity { FairSlotId = id, Name = person.Name, Email = person.Email, BirthDate = person.BirthDate, Parish = person.Parish });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapPut("/participants/{participantId:guid}", async (Guid participantId, ParticipantRequest req, AppDbContext db, HttpContext http) =>
        {
            var p = await db.Participants.FindAsync(participantId);
            if (p is null || !Access.Slot(http).InScope(p.Parish)) return Results.NotFound();
            if (Check(req, out var person) is { } error) return error;
            if (!Access.Slot(http).InScope(person.Parish)) return Endpoints.Errors("parishNotOwn");
            if (!p.Name.Equals(person.Name, StringComparison.OrdinalIgnoreCase) && await Database.IsParticipantAsync(db, p.FairSlotId, person.Name))
                return Endpoints.Errors("alreadyListed");
            (p.Name, p.Email, p.BirthDate, p.Parish) = (person.Name, person.Email, person.BirthDate, person.Parish);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapDelete("/participants/{participantId:guid}", async (Guid participantId, AppDbContext db, HttpContext http) =>
        {
            var p = await db.Participants.FindAsync(participantId);
            if (p is null || !Access.Slot(http).InScope(p.Parish)) return Results.NotFound();
            db.Participants.Remove(p);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // The list as CSV (also the template for the import).
        admin.MapGet("/slots/{id:guid}/participants.csv", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            var people = (await db.Participants.Where(p => p.FairSlotId == id).OrderBy(p => p.Name).ToListAsync()).Where(p => Access.Slot(http).InScope(p.Parish))
                .Select(p => new ParticipantCsv.Person(p.Name, p.Email, p.BirthDate, p.Parish));
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ParticipantCsv.Write(people))).ToArray(),
                "text/csv; charset=utf-8", "teilnehmende.csv");
        });

        // Preview (commit=false) or take over (commit=true). Known names are updated with the new details, others are added.
        admin.MapPost("/slots/{id:guid}/participants/import", async (Guid id, ParticipantImportRequest req, AppDbContext db, HttpContext http) =>
        {
            if (!await db.FairSlots.AnyAsync(s => s.Id == id)) return Results.NotFound();
            var result = ParticipantCsv.Parse(req.Csv ?? "");
            var existing = await db.Participants.Where(p => p.FairSlotId == id).ToListAsync();
            ParticipantEntity? Known(string name) => existing.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            // Pfarrgemeinde admins may only import people of their own Pfarrgemeinden (and only touch those already listed).
            var access = Access.Slot(http);
            var errors = result.Errors.ToList();
            var csvRows = (req.Csv ?? "").TrimStart('﻿').Split('\n');
            foreach (var person in result.People.Where(p => !access.InScope(p.Parish) || (Known(p.Name) is { } k && !access.InScope(k.Parish))))
                errors.Add(new("csvParishNotOwn", new Dictionary<string, object> { ["value"] = person.Name, ["row"] = RowOf(csvRows, person.Name) }));

            var updated = result.People.Count(p => Known(p.Name) is not null);
            var preview = new ParticipantImportDto(result.People, errors, result.People.Count - updated, updated, false);
            if (!req.Commit || errors.Count > 0 || result.People.Count == 0) return Results.Ok(preview);

            foreach (var person in result.People)
            {
                if (Known(person.Name) is { } p)
                {
                    // Only fill in what the list contains; empty cells keep the stored value.
                    p.Email = person.Email ?? p.Email;
                    p.BirthDate = person.BirthDate ?? p.BirthDate;
                    p.Parish = person.Parish ?? p.Parish;
                }
                else db.Participants.Add(new ParticipantEntity { FairSlotId = id, Name = person.Name, Email = person.Email, BirthDate = person.BirthDate, Parish = person.Parish });
            }
            await db.SaveChangesAsync();
            return Results.Ok(preview with { Saved = true });
        });

        // Prepares invitation e-mails with the personal link for everyone with an e-mail address who has none yet.
        admin.MapPost("/slots/{id:guid}/invitations", async (Guid id, AppDbContext db, HttpContext ctx, IConfiguration cfg) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var baseUrl = BaseUrl(ctx.Request, cfg);
            var access = Access.Slot(ctx);
            var due = (await db.Participants.Where(p => p.FairSlotId == id && p.Email != null && p.InvitedAt == null).ToListAsync())
                .Where(p => access.InScope(p.Parish)).ToList();
            foreach (var p in due)
            {
                db.Mails.Add(Invitation(slot, p, baseUrl));
                p.InvitedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { created = due.Count });
        });

        // Reminder with the personal link for everyone with an e-mail address who has not voted yet (only while registration is open).
        admin.MapPost("/slots/{id:guid}/reminders", async (Guid id, AppDbContext db, HttpContext ctx, IConfiguration cfg) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.Phase != Phase.Open) return Endpoints.Errors("remindersNeedOpen");
            var baseUrl = BaseUrl(ctx.Request, cfg);
            var access = Access.Slot(ctx);
            var voted = (await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample).Select(b => b.Name).ToListAsync())
                .Select(n => n.ToLowerInvariant()).ToHashSet();
            var due = (await db.Participants.Where(p => p.FairSlotId == id && p.Email != null).ToListAsync())
                .Where(p => access.InScope(p.Parish) && !voted.Contains(p.Name.ToLowerInvariant())).ToList();
            foreach (var p in due)
            {
                db.Mails.Add(Reminder(slot, p, baseUrl));
                p.InvitedAt ??= DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { created = due.Count });
        });

        admin.MapGet("/slots/{id:guid}/mails", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            var access = Access.Slot(http);
            var inScope = (await db.Participants.Where(p => p.FairSlotId == id).Select(p => new { p.Id, p.Parish }).ToListAsync())
                .Where(p => access.InScope(p.Parish)).Select(p => (Guid?)p.Id).ToHashSet();
            return (await db.Mails.Where(m => m.FairSlotId == id).ToListAsync())
                .Where(m => access.Scope is null || inScope.Contains(m.ParticipantId))
                .OrderByDescending(m => m.CreatedAt).Select(m => new MailDto(m.Id, m.To, m.Subject, m.Body, m.CreatedAt));
        });
    }

    public static void MapParticipantLinks(this WebApplication app)
    {
        // What the link page needs before the check: which vote, and whether a date of birth is asked.
        app.MapGet("/api/p/{token}", async (string token, AppDbContext db) =>
        {
            var p = await db.Participants.AsNoTracking().FirstOrDefaultAsync(x => x.Token == token && token != "");
            if (p is null) return Results.NotFound();
            var slot = await db.FairSlots.AsNoTracking().FirstAsync(s => s.Id == p.FairSlotId);
            return Results.Ok(new LinkInfoDto(slot.Id, slot.Name, p.BirthDate is not null));
        });

        app.MapPost("/api/p/{token}/check", async (string token, LinkCheckRequest req, AppDbContext db, IMemoryCache cache) =>
        {
            var p = await Verify(db, cache, token, req.BirthDate);
            return p is null ? Endpoints.Errors("checkFailed") : Results.Ok(new { name = p.Name, slotId = p.FairSlotId });
        });
    }

    /// <summary>
    /// The participant's name for a request: from the personal link (headers), or in demo mode from the request body.
    /// Null means the request is not allowed.
    /// </summary>
    public static async Task<string?> ResolveNameAsync(HttpContext http, string? bodyName)
    {
        var token = http.Request.Headers["X-Participant"].ToString();
        if (token.Length > 0)
        {
            var services = http.RequestServices;
            DateOnly? birth = DateOnly.TryParse(http.Request.Headers["X-Participant-Check"].ToString(), out var d) ? d : null;
            var p = await Verify(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<IMemoryCache>(), token, birth);
            return p?.Name;
        }
        var name = (bodyName ?? "").Trim();
        return Database.IsDemo(http.RequestServices.GetRequiredService<IConfiguration>()) && name.Length > 0 ? name : null;
    }

    /// <summary>Finds the participant for a token and checks the date of birth. Repeated wrong dates lock the link for a while.</summary>
    private static async Task<ParticipantEntity?> Verify(AppDbContext db, IMemoryCache cache, string token, DateOnly? birthDate)
    {
        if (token.Length == 0) return null;
        var key = "fails:" + token;
        var fails = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15); return 0; });
        if (fails >= MaxFailedChecks) return null;

        var p = await db.Participants.AsNoTracking().FirstOrDefaultAsync(x => x.Token == token);
        if (p is null) return null;
        if (p.BirthDate is not null && p.BirthDate != birthDate)
        {
            cache.Set(key, fails + 1, TimeSpan.FromMinutes(15));
            return null;
        }
        return p;
    }

    /// <summary>Row number (1 = header) of the first CSV line mentioning the name; for error messages only.</summary>
    private static int RowOf(string[] rows, string name)
    {
        var first = name.Split(' ')[0];
        var i = Array.FindIndex(rows, 1, r => r.Contains(name, StringComparison.OrdinalIgnoreCase) || r.Contains(first, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? 0 : i + 1;
    }

    private static IResult? Check(ParticipantRequest req, out ParticipantCsv.Person person)
    {
        var name = (req.Name ?? "").Trim();
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        var parish = string.IsNullOrWhiteSpace(req.Parish) ? null : Parishes.Match(req.Parish);
        person = new(name, email, req.BirthDate, parish);
        if (name.Length == 0) return Endpoints.Errors("nameRequired");
        if (name.Length > Validation.MaxShortText) return Results.BadRequest(new ErrorsDto([new("textTooLong", new Dictionary<string, object> { ["max"] = Validation.MaxShortText })]));
        if (email is not null && !ParticipantCsv.IsEmail(email)) return Endpoints.Errors("emailInvalid");
        if (!string.IsNullOrWhiteSpace(req.Parish) && parish is null)
            return Results.BadRequest(new ErrorsDto([new("parishUnknown", new Dictionary<string, object> { ["value"] = req.Parish.Trim() })]));
        return null;
    }

    /// <summary>Public address of the app: App:BaseUrl if set, else the request's host (https except on localhost).</summary>
    private static string BaseUrl(HttpRequest http, IConfiguration cfg) =>
        cfg["App:BaseUrl"]?.TrimEnd('/') ??
        $"{(http.Host.Host is "localhost" or "127.0.0.1" ? http.Scheme : "https")}://{http.Host}";

    private static MailEntity Reminder(FairSlotEntity slot, ParticipantEntity p, string baseUrl)
    {
        var body = new StringBuilder()
            .Append($"Hallo {p.Name},\n\n")
            .Append($"für „{slot.Name}“ haben wir von dir noch keine Wahl bekommen. Du verteilst Punkte auf das, was dir wichtig ist; ")
            .Append("wann du abstimmst, macht keinen Unterschied, nur vor dem Anmeldeschluss muss es sein.\n\n")
            .Append($"Dein persönlicher Link:\n{baseUrl}/p/{p.Token}\n\n");
        if (slot.RegistrationDeadline is { } d) body.Append($"Anmeldeschluss: {d:dd.MM.yyyy, HH:mm} Uhr\n");
        body.Append("\nBitte gib den Link nicht weiter.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
        return new MailEntity
        {
            FairSlotId = slot.Id, ParticipantId = p.Id, To = p.Email!,
            Subject = $"Erinnerung: {slot.Name}", Body = body.ToString(),
        };
    }

    private static MailEntity Invitation(FairSlotEntity slot, ParticipantEntity p, string baseUrl)
    {
        var body = new StringBuilder()
            .Append($"Hallo {p.Name},\n\n")
            .Append($"bei „{slot.Name}“ kannst du jetzt deine Veranstaltungen wählen. Du verteilst Punkte auf das, was dir wichtig ist; ")
            .Append("zugeteilt wird erst nach dem Anmeldeschluss, wann du abstimmst, macht keinen Unterschied.\n\n")
            .Append($"Dein persönlicher Link:\n{baseUrl}/p/{p.Token}\n\n");
        if (p.BirthDate is not null) body.Append("Zur Sicherheit wirst du nach deinem Geburtsdatum gefragt.\n");
        if (slot.RegistrationDeadline is { } d) body.Append($"Anmeldeschluss: {d:dd.MM.yyyy, HH:mm} Uhr\n");
        body.Append("\nBitte gib den Link nicht weiter.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
        return new MailEntity
        {
            FairSlotId = slot.Id, ParticipantId = p.Id, To = p.Email!,
            Subject = $"Deine Anmeldung: {slot.Name}", Body = body.ToString(),
        };
    }
}
