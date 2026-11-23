using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record ParticipantListDto(Guid Id, string Name, string? Email, DateOnly? BirthDate, string? Parish, string Token, DateTime? InvitedAt, bool HasVoted,
    DateOnly? BaptismDate);
public record ParticipantRequest(string? Name, string? Email, DateOnly? BirthDate, string? Parish, DateOnly? BaptismDate = null);
public record ParticipantImportRequest(string? Csv, bool Commit);
public record ParticipantImportDto(IReadOnlyList<ParticipantCsv.Person> People, IReadOnlyList<ValidationError> Errors, int New, int Updated, bool Saved);
public record ParticipantCopyRequest(Guid From, bool Commit);
/// <param name="Known">Already on the list; skipped.</param>
public record ParticipantCopyDto(int New, int Known, bool Saved);
/// <param name="From">On whose behalf it went out: the organiser's address, for mails to the organiser the participant who
/// caused it; null for the app itself.</param>
public record MailDto(Guid Id, string To, string Subject, string Body, DateTime CreatedAt, string Status, DateTime? SentAt, string? Error, string? ReplyTo,
    string? From = null);
/// <param name="Roles">organiser, leader, participant or other (only an address mails went to).</param>
public record MailboxDto(string Address, string Name, IReadOnlyList<string> Roles);
/// <param name="NeedsCheck">The date of birth is asked.</param>
/// <param name="NeedsBaptism">The date of baptism is asked.</param>
public record LinkInfoDto(Guid SlotId, string SlotName, bool NeedsCheck, bool NeedsBaptism);
public record LinkCheckRequest(DateOnly? BirthDate, DateOnly? BaptismDate = null);

/// <summary>
/// Participant list (by hand and via CSV), invitations with personal links, and the sign-in via such a link.
/// A link is /p/{token}; if a date of birth and/or baptism is stored, it must be confirmed. Afterwards the browser sends
/// token and dates with every participant request (headers X-Participant, X-Participant-Check, X-Participant-Baptism),
/// so nothing is kept on the server.
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
                .Select(p => new ParticipantListDto(p.Id, p.Name, p.Email, p.BirthDate, p.Parish, p.Token, p.InvitedAt, voted.Contains(p.Name.ToLower()), p.BaptismDate));
        });

        admin.MapPost("/slots/{id:guid}/participants", async (Guid id, ParticipantRequest req, AppDbContext db, HttpContext http) =>
        {
            if (!await db.FairSlots.AnyAsync(s => s.Id == id)) return Results.NotFound();
            if (Check(req, out var person) is { } error) return error;
            if (!Access.Slot(http).InScope(person.Parish)) return Endpoints.Errors("parishNotOwn");
            if (await Database.IsParticipantAsync(db, id, person.Name)) return Endpoints.Errors("alreadyListed");
            var entity = new ParticipantEntity { FairSlotId = id, Name = person.Name, Email = person.Email, BirthDate = person.BirthDate, BaptismDate = person.BaptismDate, Parish = person.Parish };
            db.Participants.Add(entity);
            await AllocationEndpoints.LogChangeAsync(db, http, id, "listed", person.Name, undo: Undo.Json(new(ParticipantIds: [entity.Id])));
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
            var renamed = !p.Name.Equals(person.Name, StringComparison.Ordinal);
            await AllocationEndpoints.LogChangeAsync(db, http, p.FairSlotId, renamed ? "renamed" : "participantChanged", person.Name, p.Name, person.Name,
                Undo.Json(new(Participants: [Undo.Copy(p)], ParticipantIds: [p.Id])));
            (p.Name, p.Email, p.BirthDate, p.BaptismDate, p.Parish) = (person.Name, person.Email, person.BirthDate, person.BaptismDate, person.Parish);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapDelete("/participants/{participantId:guid}", async (Guid participantId, AppDbContext db, HttpContext http) =>
        {
            var p = await db.Participants.FindAsync(participantId);
            if (p is null || !Access.Slot(http).InScope(p.Parish)) return Results.NotFound();
            await AllocationEndpoints.LogChangeAsync(db, http, p.FairSlotId, "unlisted", p.Name, undo: Undo.Json(new(Participants: [Undo.Copy(p)])));
            db.Participants.Remove(p);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // The list as CSV (also the template for the import).
        admin.MapGet("/slots/{id:guid}/participants.csv", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            var people = (await db.Participants.Where(p => p.FairSlotId == id).OrderBy(p => p.Name).ToListAsync()).Where(p => Access.Slot(http).InScope(p.Parish))
                .Select(p => new ParticipantCsv.Person(p.Name, p.Email, p.BirthDate, p.Parish, p.BaptismDate));
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

            var known = result.People.Select(p => Known(p.Name)).OfType<ParticipantEntity>().Distinct().ToList();
            var undo = known.Select(Undo.Copy).ToList();
            var touched = known.Select(p => p.Id).ToList();
            foreach (var person in result.People)
            {
                if (Known(person.Name) is { } p)
                {
                    // Only fill in what the list contains; empty cells keep the stored value.
                    p.Email = person.Email ?? p.Email;
                    p.BirthDate = person.BirthDate ?? p.BirthDate;
                    p.BaptismDate = person.BaptismDate ?? p.BaptismDate;
                    p.Parish = person.Parish ?? p.Parish;
                }
                else
                {
                    var entity = new ParticipantEntity { FairSlotId = id, Name = person.Name, Email = person.Email, BirthDate = person.BirthDate, BaptismDate = person.BaptismDate, Parish = person.Parish };
                    db.Participants.Add(entity);
                    touched.Add(entity.Id);
                }
            }
            await AllocationEndpoints.LogChangeAsync(db, http, id, "participantsImported", null, updated.ToString(), (result.People.Count - updated).ToString(),
                Undo.Json(new(Participants: undo, ParticipantIds: touched)));
            await db.SaveChangesAsync();
            return Results.Ok(preview with { Saved = true });
        });

        // Takes over the people of another vote the account sees (e.g. last year's, also ended): preview (commit=false) or
        // add (commit=true). New personal links, not invited yet; names already listed are skipped. Pfarrgemeinde admins
        // take over only people of their own Pfarrgemeinden.
        admin.MapPost("/slots/{id:guid}/participants/copy", async (Guid id, ParticipantCopyRequest req, AppDbContext db, HttpContext http) =>
        {
            var account = Access.Account(http);
            var source = await db.FairSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.From);
            if (source is null || source.Id == id || !Access.CanSee(account, source)) return Results.NotFound();
            var (from, to) = (Access.For(account, source), Access.Slot(http));
            var listed = (await db.Participants.Where(p => p.FairSlotId == id).Select(p => p.Name).ToListAsync())
                .Select(n => n.ToLowerInvariant()).ToHashSet();
            var people = (await db.Participants.AsNoTracking().Where(p => p.FairSlotId == source.Id).OrderBy(p => p.Name).ToListAsync())
                .Where(p => from.InScope(p.Parish) && to.InScope(p.Parish)).ToList();
            var fresh = people.Where(p => listed.Add(p.Name.ToLowerInvariant())).ToList();
            var preview = new ParticipantCopyDto(fresh.Count, people.Count - fresh.Count, false);
            if (!req.Commit || fresh.Count == 0) return Results.Ok(preview);

            var added = fresh.Select(p => new ParticipantEntity { FairSlotId = id, Name = p.Name, Email = p.Email, BirthDate = p.BirthDate, BaptismDate = p.BaptismDate, Parish = p.Parish }).ToList();
            db.Participants.AddRange(added);
            await AllocationEndpoints.LogChangeAsync(db, http, id, "participantsCopied", null, source.Name, fresh.Count.ToString(),
                Undo.Json(new(ParticipantIds: added.Select(p => p.Id).ToList())));
            await db.SaveChangesAsync();
            return Results.Ok(preview with { Saved = true });
        });

        // Prepares invitation e-mails with the personal link for everyone with an e-mail address who has none yet.
        admin.MapPost("/slots/{id:guid}/invitations", async (Guid id, AppDbContext db, HttpContext ctx) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var created = await InvitationsAsync(db, slot, Access.Slot(ctx), Mailing.BaseUrl(ctx.Request));
            await db.SaveChangesAsync();
            return Results.Ok(new { created });
        });

        // Reminder with the personal link for everyone with an e-mail address who has not voted yet (only while registration is open).
        admin.MapPost("/slots/{id:guid}/reminders", async (Guid id, AppDbContext db, HttpContext ctx) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.Phase != Phase.Open) return Endpoints.Errors("remindersNeedOpen");
            var baseUrl = Mailing.BaseUrl(ctx.Request);
            var access = Access.Slot(ctx);
            var voted = (await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample).Select(b => b.Name).ToListAsync())
                .Select(n => n.ToLowerInvariant()).ToHashSet();
            var due = (await db.Participants.Where(p => p.FairSlotId == id && p.Email != null).ToListAsync())
                .Where(p => access.InScope(p.Parish) && !voted.Contains(p.Name.ToLowerInvariant())).ToList();
            foreach (var p in due)
            {
                Mailing.Add(db, slot, Reminder(slot, p, baseUrl));
                p.InvitedAt ??= DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { created = due.Count });
        });

        // Once the allocation is visible: everyone with an e-mail address who voted gets their seats and the personal link.
        admin.MapPost("/slots/{id:guid}/result-mails", async (Guid id, AppDbContext db, HttpContext ctx) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.Phase != Phase.Published) return Endpoints.Errors("resultMailsNeedPublished");
            var created = await ResultMailsAsync(db, slot, Access.Slot(ctx), Mailing.BaseUrl(ctx.Request));
            await db.SaveChangesAsync();
            return Results.Ok(new { created });
        });

        admin.MapGet("/slots/{id:guid}/mails", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            var access = Access.Slot(http);
            var inScope = await InScopeAsync(db, id, access);
            var emails = await db.Participants.Where(p => p.FairSlotId == id && p.Email != null).ToDictionaryAsync(p => p.Id, p => p.Email!);
            var organiser = access.Slot.OrganiserEmail is { Length: > 0 } o ? o : null;
            string? From(MailEntity m) => organiser is null ? null
                : !m.To.Equals(organiser, StringComparison.OrdinalIgnoreCase) ? organiser
                : m.ParticipantId is { } p ? emails.GetValueOrDefault(p) : null;
            return (await db.Mails.Where(m => m.FairSlotId == id).ToListAsync())
                .Where(inScope)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new MailDto(m.Id, m.To, m.Subject, m.Body, Utc(m.CreatedAt), Mailing.Status(m), m.SentAt is { } sent ? Utc(sent) : null, m.Error, m.ReplyTo,
                    From(m)));
        });

        // Demo only: everyone this vote's mails go to or come from (organiser, leaders, participants), for the demo mailboxes.
        admin.MapGet("/slots/{id:guid}/mailboxes", async (Guid id, AppDbContext db, HttpContext http, IConfiguration cfg) =>
        {
            if (!Database.IsDemo(cfg)) return Results.NotFound();
            var access = Access.Slot(http);
            var boxes = new Dictionary<string, (string Address, string Name, List<string> Roles)>(StringComparer.OrdinalIgnoreCase);
            void Add(string? address, string? name, string role)
            {
                if (string.IsNullOrWhiteSpace(address)) return;
                if (!boxes.TryGetValue(address, out var box)) boxes[address] = box = (address, string.IsNullOrWhiteSpace(name) ? address : name, []);
                if (!box.Roles.Contains(role)) box.Roles.Add(role);
            }
            Add(access.Slot.OrganiserEmail, access.Slot.Organiser, "organiser");
            foreach (var a in await db.Activities.Where(a => a.FairSlotId == id && a.LeaderEmail != null).OrderBy(a => a.SortOrder).ToListAsync())
                Add(a.LeaderEmail, a.Leader, "leader");
            foreach (var p in (await db.Participants.Where(p => p.FairSlotId == id && p.Email != null).OrderBy(p => p.Name).ToListAsync()).Where(p => access.InScope(p.Parish)))
                Add(p.Email, p.Name, "participant");
            var inScope = await InScopeAsync(db, id, access);
            foreach (var to in (await db.Mails.Where(m => m.FairSlotId == id).ToListAsync()).Where(inScope).Select(m => m.To).Distinct())
                if (!boxes.ContainsKey(to)) Add(to, null, "other");
            return Results.Ok(boxes.Values.Select(b => new MailboxDto(b.Address, b.Name, b.Roles)));
        });

        // Tries a mail that could not be sent again at once.
        admin.MapPost("/slots/{id:guid}/mails/{mailId:guid}/retry", async (Guid id, Guid mailId, AppDbContext db, HttpContext http) =>
        {
            var mail = await db.Mails.SingleOrDefaultAsync(m => m.Id == mailId && m.FairSlotId == id);
            if (mail is null || !(await InScopeAsync(db, id, Access.Slot(http)))(mail)) return Results.NotFound();
            Mailing.Retry(mail);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Mail times are stored in UTC; the database returns them without a kind, so the browser would read them as local time.</summary>
    private static DateTime Utc(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc);

    /// <summary>Accounts limited to Pfarrgemeinden see only mails to their participants (not those to leaders).</summary>
    private static async Task<Func<MailEntity, bool>> InScopeAsync(AppDbContext db, Guid slotId, SlotAccess access)
    {
        if (access.Scope is null) return _ => true;
        var ids = (await db.Participants.Where(p => p.FairSlotId == slotId).Select(p => new { p.Id, p.Parish }).ToListAsync())
            .Where(p => access.InScope(p.Parish)).Select(p => (Guid?)p.Id).ToHashSet();
        return m => ids.Contains(m.ParticipantId);
    }

    public static void MapParticipantLinks(this WebApplication app)
    {
        // What the link page needs before the check: which vote, and which dates are asked.
        app.MapGet("/api/p/{token}", async (string token, AppDbContext db) =>
        {
            var p = await db.Participants.AsNoTracking().FirstOrDefaultAsync(x => x.Token == token && token != "");
            if (p is null) return Results.NotFound();
            var slot = await db.FairSlots.AsNoTracking().FirstAsync(s => s.Id == p.FairSlotId);
            return Results.Ok(new LinkInfoDto(slot.Id, slot.Name, p.BirthDate is not null, p.BaptismDate is not null));
        });

        app.MapPost("/api/p/{token}/check", async (string token, LinkCheckRequest req, AppDbContext db, IMemoryCache cache) =>
        {
            var p = await Verify(db, cache, token, req.BirthDate, req.BaptismDate);
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
            DateOnly? baptism = DateOnly.TryParse(http.Request.Headers["X-Participant-Baptism"].ToString(), out var t) ? t : null;
            var p = await Verify(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<IMemoryCache>(), token, birth, baptism);
            return p?.Name;
        }
        var name = (bodyName ?? "").Trim();
        return Database.IsDemo(http.RequestServices.GetRequiredService<IConfiguration>()) && name.Length > 0 ? name : null;
    }

    /// <summary>Finds the participant for a token and checks the stored dates. Repeated wrong dates lock the link for a while.</summary>
    private static async Task<ParticipantEntity?> Verify(AppDbContext db, IMemoryCache cache, string token, DateOnly? birthDate, DateOnly? baptismDate)
    {
        if (token.Length == 0) return null;
        var key = "fails:" + token;
        var fails = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15); return 0; });
        if (fails >= MaxFailedChecks) return null;

        var p = await db.Participants.AsNoTracking().FirstOrDefaultAsync(x => x.Token == token);
        if (p is null) return null;
        if ((p.BirthDate is not null && p.BirthDate != birthDate) || (p.BaptismDate is not null && p.BaptismDate != baptismDate))
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
        person = new(name, email, req.BirthDate, parish, req.BaptismDate);
        if (name.Length == 0) return Endpoints.Errors("nameRequired");
        if (name.Length > Validation.MaxShortText) return Results.BadRequest(new ErrorsDto([new("textTooLong", new Dictionary<string, object> { ["max"] = Validation.MaxShortText })]));
        if (email is not null && !ParticipantCsv.IsEmail(email)) return Endpoints.Errors("emailInvalid");
        if (!string.IsNullOrWhiteSpace(req.Parish) && parish is null)
            return Results.BadRequest(new ErrorsDto([new("parishUnknown", new Dictionary<string, object> { ["value"] = req.Parish.Trim() })]));
        return null;
    }

    /// <summary>Invitations for everyone in scope with an e-mail address and no invitation yet. Saved by the caller.</summary>
    public static async Task<int> InvitationsAsync(AppDbContext db, FairSlotEntity slot, SlotAccess access, string baseUrl)
    {
        var due = (await db.Participants.Where(p => p.FairSlotId == slot.Id && p.Email != null && p.InvitedAt == null).OrderBy(p => p.Name).ToListAsync())
            .Where(p => access.InScope(p.Parish)).ToList();
        foreach (var p in due)
        {
            Mailing.Add(db, slot, Invitation(slot, p, baseUrl));
            p.InvitedAt = DateTime.UtcNow;
        }
        return due.Count;
    }

    /// <summary>Everyone in scope with an e-mail address who voted gets their seats and the personal link. Saved by the caller.</summary>
    public static async Task<int> ResultMailsAsync(AppDbContext db, FairSlotEntity slot, SlotAccess access, string baseUrl)
    {
        var (allocation, activities, ballots) = await AllocationEndpoints.CurrentAsync(db, slot);
        var byName = ballots.Where(b => !b.IsSample).GroupBy(b => b.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var due = (await db.Participants.Where(p => p.FairSlotId == slot.Id && p.Email != null).OrderBy(p => p.Name).ToListAsync())
            .Where(p => access.InScope(p.Parish) && byName.ContainsKey(p.Name.ToLowerInvariant())).ToList();
        foreach (var p in due)
        {
            var seats = allocation.Assignments.GetValueOrDefault(byName[p.Name.ToLowerInvariant()].Id.ToString(), []).Select(x => x.Session.Id);
            Mailing.Add(db, slot, Result(slot, p, AllocationEndpoints.SeatText(activities, seats), baseUrl));
        }
        return due.Count;
    }

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
        if (p.BirthDate is not null || p.BaptismDate is not null)
            body.Append($"Zur Sicherheit wirst du nach deinem {(p.BirthDate is null ? "Taufdatum" : p.BaptismDate is null ? "Geburtsdatum" : "Geburts- und Taufdatum")} gefragt.\n");
        if (slot.RegistrationDeadline is { } d) body.Append($"Anmeldeschluss: {d:dd.MM.yyyy, HH:mm} Uhr\n");
        body.Append("\nBitte gib den Link nicht weiter.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
        return new MailEntity
        {
            FairSlotId = slot.Id, ParticipantId = p.Id, To = p.Email!,
            Subject = $"Deine Anmeldung: {slot.Name}", Body = body.ToString(),
        };
    }

    private static MailEntity Result(FairSlotEntity slot, ParticipantEntity p, string seats, string baseUrl)
    {
        var body = new StringBuilder()
            .Append($"Hallo {p.Name},\n\n")
            .Append($"die Zuteilung für „{slot.Name}“ steht. {(seats.Length > 0 ? $"Deine Plätze: {seats}." : "Leider hast du noch keinen Platz bekommen.")}\n\n")
            .Append("Unter deinem persönlichen Link siehst du alle Termine. Dort kannst du auch einen Platz zurückgeben, den du nicht mehr willst, ")
            .Append("oder dich vormerken, falls woanders etwas frei wird:\n")
            .Append($"{baseUrl}/p/{p.Token}\n")
            .Append("\nBitte gib den Link nicht weiter.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
        return new MailEntity { ParticipantId = p.Id, To = p.Email!, Subject = $"Deine Zuteilung: {slot.Name}", Body = body.ToString() };
    }
}
