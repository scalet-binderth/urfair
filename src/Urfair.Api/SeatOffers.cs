using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

/// <param name="Status">open, accepted, declined, expired or withdrawn.</param>
public record OfferDto(Guid Id, string SessionId, string Person, string Status, string? GivesUp, DateTime ExpiresAt, DateTime CreatedAt);
public record MyOfferDto(Guid Id, string ActivityId, string Title, DateTime Start, DateTime? End, bool Flexible, string Location, string? GivesUp, DateTime ExpiresAt);
public record OfferRequest(string SessionId);
public record OfferAnswerRequest(string Name);
/// <param name="Need">The person lacks a seat in the group (otherwise they ticked it).</param>
public record NextPersonDto(string Person, string? GivesUp, bool Need);
public record OfferPreviewDto(string SessionId, IReadOnlyList<NextPersonDto> Next);

/// <summary>
/// Filling a free seat after the lists are final (Closed): when someone cancels, the seat is offered at once (setting
/// "Bei Absage automatisch nachbesetzen", on by default), otherwise organisers start it per date. The seat is offered to one
/// person at a time in the order of moving up (missing a seat before a tick, more points, lottery). Only real people are
/// asked, each once per date, and nobody with another open offer. The closer the date, the shorter the time to answer
/// (<see cref="AnswerTimeFor"/>), at most until a day before the date; dates less than 36 hours ahead are not offered.
/// Declined or expired: the next person is asked. Accepted: the seat they give up is offered next (a chain). When the phase
/// changes, open offers are withdrawn.
/// </summary>
public static class SeatOffers
{
    static readonly TimeSpan BeforeStart = TimeSpan.FromHours(24);
    /// <summary>A day before the date plus the shortest time to answer.</summary>
    static readonly TimeSpan MinAhead = TimeSpan.FromHours(36);
    const int PreviewSize = 3;

    /// <summary>48 hours; 24 hours if the date is less than a week ahead; 12 hours if less than three days.</summary>
    public static TimeSpan AnswerTimeFor(DateTime start, DateTime now) => (start - now) switch
    {
        var ahead when ahead < TimeSpan.FromDays(3) => TimeSpan.FromHours(12),
        var ahead when ahead < TimeSpan.FromDays(7) => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(48),
    };

    public static void MapSeatOfferAdmin(this RouteGroupBuilder admin)
    {
        admin.MapGet("/slots/{id:guid}/offers", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            if (await db.FairSlots.FindAsync(id) is null) return Results.NotFound();
            var sees = await Access.SeesPersonAsync(http, db, id);
            var names = await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample).ToDictionaryAsync(b => b.Id, b => b.Name);
            var activities = await Database.LoadActivitiesAsync(db, id);
            return Results.Ok((await db.SeatOffers.AsNoTracking().Where(o => o.FairSlotId == id).ToListAsync())
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new OfferDto(o.Id, o.SessionId, names.GetValueOrDefault(o.BallotId, "?") is var n && sees(n) ? n : Access.HiddenName, o.Status.ToString().ToLowerInvariant(),
                    o.GivesUpSessionId is { } g ? AllocationEndpoints.SeatText(activities, [g]) : null, o.ExpiresAt,
                    Database.Utc(o.CreatedAt))));
        });

        // „Platz nachbesetzen“: offers one free seat of this date to the next person.
        admin.MapPost("/slots/{id:guid}/offers", async (Guid id, OfferRequest req, AppDbContext db, HttpContext http) =>
        {
            if (await db.FairSlots.FindAsync(id) is not { } slot) return Results.NotFound();
            var error = await StartAsync(db, slot, req.SessionId, Access.Account(http).Name, Mailing.BaseUrl(http.Request));
            return error is null ? Results.NoContent() : Endpoints.Errors(error);
        });

        // Who would be asked next for each date with a free seat (not yet offered), so organisers see what a cancellation means.
        admin.MapGet("/slots/{id:guid}/offers/next", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            if (await db.FairSlots.FindAsync(id) is not { } slot) return Results.NotFound();
            var sees = await Access.SeesPersonAsync(http, db, id);
            return Results.Ok((await PreviewAsync(db, slot))
                .Select(p => p with { Next = p.Next.Select(n => sees(n.Person) ? n : n with { Person = Access.HiddenName }).ToList() }));
        });
    }

    /// <summary>Per date still offered and with a free seat nobody holds an offer for: the next people in offer order.</summary>
    public static async Task<List<OfferPreviewDto>> PreviewAsync(AppDbContext db, FairSlotEntity slot)
    {
        if (slot.Phase != Phase.Closed || slot.FrozenAt is null) return [];
        var (allocation, activities, ballots) = await AllocationEndpoints.CurrentAsync(db, slot);
        var offers = await db.SeatOffers.AsNoTracking().Where(o => o.FairSlotId == slot.Id).ToListAsync();
        var open = offers.Where(o => o.Status == OfferStatus.Open).ToList();
        var samples = ballots.Where(b => b.IsSample).Select(b => b.Id.ToString()).ToHashSet();
        var names = ballots.ToDictionary(b => b.Id.ToString(), b => b.Name);
        var now = Phases.Now();
        var result = new List<OfferPreviewDto>();
        foreach (var session in activities.SelectMany(a => a.Sessions))
        {
            if (session.Capacity is not { } c || session.Start < now + MinAhead) continue;
            if (c - allocation.Load(session) - open.Count(o => o.SessionId == session.Id) <= 0) continue;
            var skip = offers.Where(o => o.SessionId == session.Id).Select(o => o.BallotId.ToString())
                .Concat(open.Select(o => o.BallotId.ToString())).Concat(samples).ToHashSet();
            var next = new List<NextPersonDto>();
            while (next.Count < PreviewSize && allocation.NextFor(session, slot.PerParticipant, slot.Seed, b => skip.Contains(b.Id)) is { } m)
            {
                skip.Add(m.BallotId);
                next.Add(new NextPersonDto(names[m.BallotId], m.GaveUp is { } g ? AllocationEndpoints.SeatText(activities, [g.Id]) : null, m.Need));
            }
            result.Add(new OfferPreviewDto(session.Id, next));
        }
        return result;
    }

    /// <summary>Offers a free seat of the session to the next person; returns an error code if it cannot.</summary>
    public static async Task<string?> StartAsync(AppDbContext db, FairSlotEntity slot, string sessionId, string actor, string? baseUrl)
    {
        if (slot.Phase != Phase.Closed || slot.FrozenAt is null) return "offersClosed";
        var (allocation, activities, ballots) = await AllocationEndpoints.CurrentAsync(db, slot);
        if (activities.SelectMany(a => a.Sessions).FirstOrDefault(s => s.Id == sessionId) is not { } session) return "unknownSession";
        var now = Phases.Now();
        if (session.Start < now + MinAhead) return "offerTooLate";
        var offers = await db.SeatOffers.Where(o => o.FairSlotId == slot.Id).ToListAsync();
        var open = offers.Where(o => o.Status == OfferStatus.Open).ToList();
        if (session.Capacity is { } c && c - allocation.Load(session) - open.Count(o => o.SessionId == sessionId) <= 0) return "noFreeSeat";

        var asked = offers.Where(o => o.SessionId == sessionId).Select(o => o.BallotId.ToString())
            .Concat(open.Select(o => o.BallotId.ToString())).ToHashSet();
        var samples = ballots.Where(b => b.IsSample).Select(b => b.Id.ToString()).ToHashSet();
        if (allocation.NextFor(session, slot.PerParticipant, slot.Seed, b => samples.Contains(b.Id) || asked.Contains(b.Id)) is not { } next)
            return "nobodyLeft";

        var ballot = ballots.Single(b => b.Id.ToString() == next.BallotId);
        var expires = new[] { now + AnswerTimeFor(session.Start, now), session.Start - BeforeStart }.Min();
        db.SeatOffers.Add(new SeatOfferEntity
        {
            FairSlotId = slot.Id, SessionId = sessionId, BallotId = ballot.Id, GivesUpSessionId = next.GaveUp?.Id, ExpiresAt = expires,
        });
        var givesUp = next.GaveUp is { } g ? AllocationEndpoints.SeatText(activities, [g.Id]) : null;
        AllocationEndpoints.Log(db, slot.Id, actor, "offered", ballot.Name, givesUp, AllocationEndpoints.SeatText(activities, [sessionId]));
        await OfferMailAsync(db, slot, ballot.Name, activities, session, givesUp, expires, baseUrl);
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>The person takes the seat: the seat they give up is offered next. Returns an error code if the offer is gone.</summary>
    public static async Task<string?> AcceptAsync(AppDbContext db, FairSlotEntity slot, Guid offerId, string name, string? baseUrl)
    {
        if (await MyOpenOfferAsync(db, slot, offerId, name) is not { } offer) return "offerGone";
        var (allocation, activities, ballots) = await AllocationEndpoints.CurrentAsync(db, slot);
        var session = activities.SelectMany(a => a.Sessions).Single(s => s.Id == offer.SessionId);
        var me = ballots.Single(b => b.Id == offer.BallotId).ToModel();
        var (possible, givesUp) = allocation.ExchangeFor(me, session, slot.PerParticipant);
        var others = await db.SeatOffers.CountAsync(o => o.SessionId == offer.SessionId && o.Status == OfferStatus.Open && o.Id != offer.Id);
        var free = session.Capacity is not { } c || c - allocation.Load(session) - others > 0;
        if (!possible || !free || givesUp?.Id != offer.GivesUpSessionId)
        {
            // Seats changed since the offer (e.g. by hand): it no longer holds, the next person is asked.
            await EndAsync(db, slot, offer, OfferStatus.Withdrawn, Phases.Clock, activities);
            await NextAsync(db, slot, offer.SessionId, activities, baseUrl);
            return "seatTaken";
        }

        var ballot = await db.Ballots.SingleAsync(b => b.Id == offer.BallotId);
        var seats = await db.Assignments.Where(a => a.BallotId == ballot.Id).ToListAsync();
        var before = AllocationEndpoints.SeatText(activities, seats.Select(a => a.SessionId));
        if (givesUp is not null) db.Assignments.RemoveRange(seats.Where(a => a.SessionId == givesUp.Id));
        db.Assignments.Add(new AssignmentEntity { FairSlotId = slot.Id, BallotId = ballot.Id, SessionId = session.Id });
        ballot.Waitlist = ballot.Waitlist.Where(x => x != session.ActivityId).ToList();
        ballot.KeepGiveUpForTicks();
        (offer.Status, offer.AnsweredAt) = (OfferStatus.Accepted, DateTime.UtcNow);
        var after = AllocationEndpoints.SeatText(activities, seats.Select(a => a.SessionId).Where(x => x != givesUp?.Id).Append(session.Id));
        AllocationEndpoints.Log(db, slot.Id, name, "offerAccepted", name, before, after);
        await AcceptedMailAsync(db, slot, name, activities, session, givesUp is null ? null : AllocationEndpoints.SeatText(activities, [givesUp.Id]), after);
        await db.SaveChangesAsync();
        if (givesUp is not null) await NextAsync(db, slot, givesUp.Id, activities, baseUrl);
        return null;
    }

    /// <summary>The person does not want the seat: the next person is asked.</summary>
    public static async Task<string?> DeclineAsync(AppDbContext db, FairSlotEntity slot, Guid offerId, string name, string? baseUrl)
    {
        if (await MyOpenOfferAsync(db, slot, offerId, name) is not { } offer) return "offerGone";
        var activities = await Database.LoadActivitiesAsync(db, slot.Id);
        await EndAsync(db, slot, offer, OfferStatus.Declined, name, activities);
        await NextAsync(db, slot, offer.SessionId, activities, baseUrl);
        return null;
    }

    /// <summary>Once a minute: expired offers go to the next person (the person gets a mail); outside Closed open offers are withdrawn.</summary>
    public static async Task TickAsync(AppDbContext db)
    {
        var open = await db.SeatOffers.Where(o => o.Status == OfferStatus.Open).ToListAsync();
        var now = Phases.Now();
        foreach (var group in open.GroupBy(o => o.FairSlotId))
        {
            var slot = await db.FairSlots.SingleAsync(s => s.Id == group.Key);
            var activities = await Database.LoadActivitiesAsync(db, slot.Id);
            if (slot.Phase != Phase.Closed)
            {
                foreach (var offer in group) await EndAsync(db, slot, offer, OfferStatus.Withdrawn, Phases.Clock, activities);
                continue;
            }
            foreach (var offer in group.Where(o => o.ExpiresAt <= now))
            {
                await EndAsync(db, slot, offer, OfferStatus.Expired, Phases.Clock, activities);
                await ExpiredMailAsync(db, slot, offer, activities);
                await db.SaveChangesAsync();
                await NextAsync(db, slot, offer.SessionId, activities, Mailing.KnownBaseUrl);
            }
        }
    }

    /// <summary>Leaving Closed by hand: open offers are withdrawn at once.</summary>
    public static async Task WithdrawAllAsync(AppDbContext db, FairSlotEntity slot, string actor)
    {
        var open = await db.SeatOffers.Where(o => o.FairSlotId == slot.Id && o.Status == OfferStatus.Open).ToListAsync();
        if (open.Count == 0) return;
        var activities = await Database.LoadActivitiesAsync(db, slot.Id);
        foreach (var offer in open) await EndAsync(db, slot, offer, OfferStatus.Withdrawn, actor, activities);
    }

    /// <summary>The person's open offer, if any (for the status card).</summary>
    public static async Task<MyOfferDto?> MineAsync(AppDbContext db, FairSlotEntity slot, Guid ballotId, List<Activity> activities)
    {
        if (slot.Phase != Phase.Closed) return null;
        var offer = await db.SeatOffers.AsNoTracking().FirstOrDefaultAsync(o => o.BallotId == ballotId && o.Status == OfferStatus.Open);
        if (offer is null || offer.ExpiresAt <= Phases.Now()) return null;
        var activity = activities.Single(a => a.Sessions.Any(s => s.Id == offer.SessionId));
        var s = activity.Sessions.Single(x => x.Id == offer.SessionId);
        return new MyOfferDto(offer.Id, activity.Id, activity.Title, s.Start, s.End, s.Flexible, s.Location,
            offer.GivesUpSessionId is { } g ? AllocationEndpoints.SeatText(activities, [g]) : null, offer.ExpiresAt);
    }

    static async Task<SeatOfferEntity?> MyOpenOfferAsync(AppDbContext db, FairSlotEntity slot, Guid offerId, string name)
    {
        if (slot.Phase != Phase.Closed) return null;
        var offer = await db.SeatOffers.FirstOrDefaultAsync(o => o.Id == offerId && o.FairSlotId == slot.Id && o.Status == OfferStatus.Open);
        if (offer is null || offer.ExpiresAt <= Phases.Now()) return null;
        var owner = await db.Ballots.Where(b => b.Id == offer.BallotId).Select(b => b.Name).SingleAsync();
        return owner.Equals(name, StringComparison.OrdinalIgnoreCase) ? offer : null;
    }

    static async Task EndAsync(AppDbContext db, FairSlotEntity slot, SeatOfferEntity offer, OfferStatus status, string actor, List<Activity> activities)
    {
        (offer.Status, offer.AnsweredAt) = (status, DateTime.UtcNow);
        var person = await db.Ballots.Where(b => b.Id == offer.BallotId).Select(b => b.Name).SingleAsync();
        AllocationEndpoints.Log(db, slot.Id, actor, "offer" + status, person, null, AllocationEndpoints.SeatText(activities, [offer.SessionId]));
        await db.SaveChangesAsync();
    }

    /// <summary>Offers the seat to the next person; if nobody is left (or the date is too close), the organisers are told.</summary>
    static async Task NextAsync(AppDbContext db, FairSlotEntity slot, string sessionId, List<Activity> activities, string? baseUrl)
    {
        var error = await StartAsync(db, slot, sessionId, Phases.Clock, baseUrl);
        if (error is not ("nobodyLeft" or "offerTooLate")) return;
        var seat = AllocationEndpoints.SeatText(activities, [sessionId]);
        AllocationEndpoints.Log(db, slot.Id, Phases.Clock, "offerNone", null, null, seat);
        if (!string.IsNullOrWhiteSpace(slot.OrganiserEmail))
            Mailing.Add(db, slot, new MailEntity
            {
                To = slot.OrganiserEmail, Subject = $"Platz frei: {seat} – {slot.Name}",
                Body = $"Hallo,\n\nbei „{slot.Name}“ ist ein Platz frei: {seat}.\n" +
                    (error == "offerTooLate" ? "Der Termin ist zu nah, um ihn noch anzubieten.\n" : "Niemand mehr wartet darauf oder hat noch nicht geantwortet.\n") +
                    $"Unter „Zuteilung“ kannst du jemanden von Hand einteilen oder den Platz frei lassen.\n{baseUrl}/admin/s/{slot.Id}\n\nUrfair",
            });
        await db.SaveChangesAsync();
    }

    static async Task OfferMailAsync(AppDbContext db, FairSlotEntity slot, string name, List<Activity> activities, Session session, string? givesUp,
        DateTime expires, string? baseUrl)
    {
        if (await Database.ListedAsync(db, slot.Id, name) is not { } p) return;
        Mailing.ToParticipant(db, slot, p, $"Freier Platz für dich: {slot.Name}",
            $"bei „{slot.Name}“ ist ein Platz frei geworden: {AllocationEndpoints.SeatText(activities, [session.Id])}, {When(session)}.\n" +
            (givesUp is null
                ? "Dir fehlt noch ein Platz, darum bekommst du ihn zuerst angeboten.\n"
                : $"Du hast ihn dir vorgemerkt. Nimmst du ihn, gibst du dafür deinen Platz bei {givesUp} ab.\n") +
            $"\nBitte sag bis {Mailing.Deadline(expires)} zu oder ab, sonst geht das Angebot an die nächste Person.\n",
            "Zusagen oder absagen kannst du unter deinem persönlichen Link", baseUrl);
    }

    static async Task AcceptedMailAsync(AppDbContext db, FairSlotEntity slot, string name, List<Activity> activities, Session session, string? gaveUp, string seats)
    {
        if (await Database.ListedAsync(db, slot.Id, name) is not { } p) return;
        Mailing.ToParticipant(db, slot, p, $"Platz bestätigt: {slot.Name}",
            $"danke für deine Zusage: Bei „{slot.Name}“ hast du jetzt einen Platz bei {AllocationEndpoints.SeatText(activities, [session.Id])}, {When(session)}.\n" +
            (gaveUp is null ? "" : $"Deinen Platz bei {gaveUp} hast du dafür abgegeben.\n") +
            $"\nDeine Plätze jetzt: {seats}\n");
    }

    static async Task ExpiredMailAsync(AppDbContext db, FairSlotEntity slot, SeatOfferEntity offer, List<Activity> activities)
    {
        var name = await db.Ballots.Where(b => b.Id == offer.BallotId).Select(b => b.Name).SingleAsync();
        if (await Database.ListedAsync(db, slot.Id, name) is not { } p) return;
        Mailing.ToParticipant(db, slot, p, $"Angebot abgelaufen: {slot.Name}",
            $"bei „{slot.Name}“ hast du dich bis {Mailing.Deadline(offer.ExpiresAt)} nicht gemeldet, " +
            $"darum ist das Angebot für {AllocationEndpoints.SeatText(activities, [offer.SessionId])} an die nächste Person gegangen.\n" +
            "Deine Plätze bleiben wie bisher.\n");
    }

    static string When(Session s) =>
        s.Flexible ? $"bis {s.Start:dd.MM.yyyy}, nach Vereinbarung" : $"am {s.Start:dd.MM.yyyy} um {s.Start:HH:mm} Uhr";
}
