using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

/// <param name="Missing">Required staffing: people still missing on this date by the current allocation (null without a seat count).</param>
public record SessionDto(string Id, DateTime Start, DateTime? End, string Location, int? Capacity, bool Flexible, int? Missing = null);
public record ActivityDto(string Id, string Title, string? RestrictedTo, int? Seats, IReadOnlyList<SessionDto> Sessions, string? GroupId, string? Description, string? Parish, string? Leader, string? LeaderEmail = null);
/// <param name="DeleteOn">Ended: the day the vote is deleted with all its data (<see cref="Retention"/>).</param>
public record SlotSummaryDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, int ActivityCount, int BallotCount, DateOnly? DeleteOn = null);
/// <summary>A mail as the participant would receive it (demo mailbox).</summary>
public record MyMailDto(string Subject, string Body, DateTime CreatedAt);
/// <param name="Excluded">Only for organisers: names a vote open to everyone leaves out.</param>
public record SlotDto(Guid Id, string Name, Phase Phase, DateTime? RegistrationDeadline, Rules Rules, int BallotCount, int SampleCount, bool AdminProtected, bool OpenToAll, string? Parish, string? Organiser,
    Visibility Visibility, bool ShowInterim = false, bool? CanEdit = null, string? OrganiserEmail = null, DateOnly? DeleteOn = null, IReadOnlyList<string>? Excluded = null,
    bool AutoFill = true);
/// <param name="Extra">"More than required": up to this many seats more (at most the vote's maximum).</param>
/// <param name="Friends">"Together with friends": the group code; everyone in the group enters it themselves.</param>
public record BallotRequest(string Name, Dictionary<string, int> Points, List<string>? UnavailableSessions, int Extra = 0, string? Friends = null);
public record ErrorsDto(IReadOnlyList<ValidationError> Errors);
public record LookupRequest(string Name);
/// <param name="GiveUp">Per ticked activity the activity whose seat goes for it; null keeps the earlier choices.</param>
public record WaitlistRequest(string Name, List<string>? ActivityIds, Dictionary<string, string>? GiveUp = null);
public record GiveBackRequest(string Name, string ActivityId, bool Replacement);
public record TakeSeatRequest(string Name, string SessionId);
/// <param name="Status">fixed, waitlist (full), free (seats left) or givenBack.</param>
/// <param name="CanMoveUp">While visible: a tick can move the person up (more points than the seat they would give up, <paramref name="GivesUp"/>).</param>
/// <param name="GiveUpOptions">While visible: the seats that could go for this activity (weakest first), to choose from if more than one.</param>
/// <param name="GiveUpChoice">The activity whose seat the person chose to give up for it, if any.</param>
public record MyActivityStatusDto(string ActivityId, string Title, string? GroupId, int Points, string Status, int? Position, bool Waitlisted,
    DateTime? Start, DateTime? End, bool Flexible, string? Location, bool CanMoveUp = false, string? GivesUp = null, bool CanGiveBack = false,
    IReadOnlyList<GiveUpOptionDto>? GiveUpOptions = null, string? GiveUpChoice = null);
public record GiveUpOptionDto(string ActivityId, string Title, int Points);
/// <summary>Seats a person lacks in a group (null without groups), with the free seats they may take.</summary>
public record MissingDto(string? GroupId, int Count, IReadOnlyList<OpenSeatDto> Open);
public record OpenSeatDto(string SessionId, string ActivityId, string Title, DateTime Start, DateTime? End, bool Flexible, string Location, int Points);
/// <param name="Offer">When closed: a free seat offered to the person, to accept or decline (<see cref="SeatOffers"/>).</param>
public record MyStatusDto(bool Voted, IReadOnlyList<MyActivityStatusDto> Activities, IReadOnlyList<string> Full, IReadOnlyList<MissingDto>? Missing = null,
    MyOfferDto? Offer = null);

/// <summary>Endpoints for participants.</summary>
public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Tells the start page whether the organisation area needs an access key.
        api.MapGet("/info", (IConfiguration cfg, HttpRequest http) =>
        {
            Mailing.BaseUrl(http); // remembered for links in mails made without a request
            return new { adminProtected = !string.IsNullOrEmpty(cfg["Admin:Key"]), demo = Database.IsDemo(cfg), mailSending = Mailing.Enabled, parishes = Parishes.All,
                version = Operations.Version };
        });
        api.MapOperations();

        // Legal notice and privacy policy of this instance (Legal:Imprint, Legal:Privacy); null: the UI shows its template.
        api.MapGet("/legal", (IConfiguration cfg) => new
        {
            imprint = string.IsNullOrWhiteSpace(cfg["Legal:Imprint"]) ? null : cfg["Legal:Imprint"],
            privacy = string.IsNullOrWhiteSpace(cfg["Legal:Privacy"]) ? null : cfg["Legal:Privacy"],
            retentionDays = Retention.Days(cfg),
        });

        // Reloads the own previously submitted ballot so it can be changed.
        api.MapPost("/slots/{id:guid}/ballots/lookup", async (Guid id, LookupRequest req, AppDbContext db, HttpContext http) =>
        {
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } who) return Results.Unauthorized();
            var name = who.ToLower();
            var ballot = await db.Ballots.Include(b => b.Points).AsNoTracking()
                .Where(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name)
                .OrderByDescending(b => b.SubmittedAt).FirstOrDefaultAsync();
            return ballot is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    points = ballot.Points.ToDictionary(p => p.ActivityId, p => p.Points), unavailableSessions = ballot.UnavailableSessions, extra = ballot.Extra,
                    friends = ballot.Friends,
                    // Everyone in the group entered the code themselves, so they may see each other's names.
                    friendNames = ballot.Friends is null ? [] : await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample && b.Friends == ballot.Friends && b.Id != ballot.Id)
                        .OrderBy(b => b.Name).Select(b => b.Name).ToListAsync(),
                });
        });

        // FairSlots this person is listed in (drafts stay hidden). Identified by personal link, in demo mode also by name.
        api.MapPost("/my-slots", async (LookupRequest req, AppDbContext db, HttpContext http, IConfiguration cfg) =>
        {
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } who) return Results.Unauthorized();
            var name = who.ToLower();
            var days = Retention.Days(cfg);
            var slots = await db.FairSlots
                .Where(s => s.Phase != Phase.Draft && (s.Participants.Any(p => p.Name.ToLower() == name) || s.OpenToAll))
                .OrderBy(s => s.Phase).ThenBy(s => s.RegistrationDeadline)
                .Select(s => new
                {
                    Dto = new SlotSummaryDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline, s.Activities.Count, s.Ballots.Count, null), s.EndedAt,
                    Listed = s.Participants.Any(p => p.Name.ToLower() == name), s.Excluded,
                })
                .ToListAsync();
            return Results.Ok(slots.Where(x => x.Listed || Database.OpenFor(x.Excluded, who))
                .Select(x => x.Dto with { DeleteOn = Retention.DeleteOn(x.EndedAt, days) }));
        });

        // The own allocation once the organisers have published it.
        api.MapPost("/slots/{id:guid}/my-result", async (Guid id, LookupRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (!Phases.ShowsResult(slot.Phase)) return Errors("notPublished");
            var result = await AllocationEndpoints.AllocateAsync(db, slot);
            var mine = result.Participants.FirstOrDefault(p => !p.IsSample && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return Results.Ok(new { voted = mine is not null, assignments = mine?.Assignments ?? [] });
        });

        // Demo only: the mails of this vote addressed to the person, as they would arrive in their inbox.
        api.MapPost("/slots/{id:guid}/my-mails", async (Guid id, LookupRequest req, AppDbContext db, HttpContext http, IConfiguration cfg) =>
        {
            if (!Database.IsDemo(cfg)) return Results.NotFound();
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (!await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            var email = await db.Participants.Where(p => p.FairSlotId == id && p.Name.ToLower() == name.ToLower() && p.Email != null)
                .Select(p => p.Email).FirstOrDefaultAsync();
            if (email is null) return Results.Ok(Array.Empty<MyMailDto>());
            return Results.Ok((await db.Mails.AsNoTracking().Where(m => m.FairSlotId == id).ToListAsync())
                .Where(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new MyMailDto(m.Subject, m.Body, DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc))));
        });

        // What the person would get if registration closed now (only if the organisers show it), later the published allocation.
        api.MapPost("/slots/{id:guid}/my-status", async (Guid id, LookupRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (!ShowsStatus(slot))
                return Results.Ok(new MyStatusDto(await db.Ballots.AnyAsync(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name.ToLower()), [], []));
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        // Which not-yet-fixed activities the person wants to move up into. While visible, a tick may move them up at once.
        // When closed ticks can only be removed (no longer asked when a seat there is offered), not added.
        api.MapPost("/slots/{id:guid}/waitlist", async (Guid id, WaitlistRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            var closed = slot.Phase == Phase.Closed && slot.FrozenAt is not null;
            if (!(closed || slot.Phase == Phase.Published || slot.Phase == Phase.Open && slot.ShowInterim)) return Errors("waitlistClosed");
            var ballot = await MyBallotAsync(db, id, name);
            if (ballot is null) return Errors("notVoted");
            if (closed && (req.ActivityIds ?? []).Any(x => !ballot.Waitlist.Contains(x))) return Errors("waitlistClosed");
            var pointed = ballot.Points.Where(p => p.Points > 0).Select(p => p.ActivityId).ToHashSet();
            ballot.Waitlist = (req.ActivityIds ?? []).Where(x => pointed.Contains(x) && !ballot.Declined.Contains(x)).Distinct().ToList();
            if (req.GiveUp is not null && slot.Phase == Phase.Published)
            {
                // Only seats that could actually go for the activity (same group, fewer points, not fixed).
                var (allocation, activities, _) = await AllocationEndpoints.CurrentAsync(db, slot);
                var me = ballot.ToModel();
                ballot.GiveUp = req.GiveUp
                    .Where(x => ballot.Waitlist.Contains(x.Key) && activities.FirstOrDefault(a => a.Id == x.Key) is { } a &&
                        allocation.GiveUpOptions(me, a, slot.PerParticipant).Any(s => s.ActivityId == x.Value))
                    .Select(x => $"{x.Key}>{x.Value}").ToList();
            }
            else ballot.KeepGiveUpForTicks();
            await db.SaveChangesAsync();
            await MovingUp.FillAsync(db, slot);
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        // Gives back a seat the person no longer wants, for a date still ahead. It is never allocated to them again.
        // While visible the next person moves up, and with a replacement the person gets a free seat of the same group they
        // gave points to, if there is one. When closed (cancelling, e.g. ill) the organisers are told, and the seat is offered
        // to the next person at once if the vote says so (SeatOffers); otherwise they decide.
        api.MapPost("/slots/{id:guid}/give-back", async (Guid id, GiveBackRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (slot.Phase is not (Phase.Published or Phase.Closed) || slot.FrozenAt is null) return Errors("giveBackClosed");
            var ballot = await MyBallotAsync(db, id, name);
            if (ballot is null) return Errors("notVoted");
            var activities = await Database.LoadActivitiesAsync(db, id);
            var sessions = activities.FirstOrDefault(a => a.Id == req.ActivityId)?.Sessions.ToDictionary(s => s.Id) ?? [];
            var seats = await db.Assignments.Where(a => a.BallotId == ballot.Id).ToListAsync();
            if (seats.FirstOrDefault(a => sessions.ContainsKey(a.SessionId)) is not { } seat) return Errors("noSeat");
            if (sessions[seat.SessionId].Start <= Phases.Now()) return Errors("tooLate");

            var before = AllocationEndpoints.SeatText(activities, seats.Select(a => a.SessionId));
            db.Assignments.Remove(seat);
            var after = AllocationEndpoints.SeatText(activities, seats.Where(a => a != seat).Select(a => a.SessionId));
            ballot.Declined = [.. ballot.Declined.Append(req.ActivityId).Distinct()];
            ballot.Waitlist = ballot.Waitlist.Where(x => x != req.ActivityId).ToList();
            ballot.KeepGiveUpForTicks();
            var group = Allocator.FromAssignments(activities, [], [], slot.ToRules().Groups).GroupKey(req.ActivityId);
            ballot.NoReplacement = req.Replacement
                ? ballot.NoReplacement.Where(x => x != group).ToList()
                : [.. ballot.NoReplacement.Append(group).Distinct()];
            var closed = slot.Phase == Phase.Closed;
            var given = AllocationEndpoints.SeatText(activities, [seat.SessionId]);
            AllocationEndpoints.Log(db, id, name, closed ? "cancelled" : "givenBack", name, before, after);
            if (closed)
            {
                await db.SaveChangesAsync();
                var fill = slot.AutoFill && sessions[seat.SessionId].Capacity is not null
                    ? await SeatOffers.StartAsync(db, slot, seat.SessionId, Phases.Clock, Mailing.BaseUrl(http.Request)) ?? "offered"
                    : null;
                await OrganiserMailAsync(db, slot, name, given, after, req.Replacement, null, http, fill);
            }
            await db.SaveChangesAsync();
            await MovingUp.FillAsync(db, slot);
            var status = await StatusAsync(db, slot, name);
            // While visible the organisers only hear about it if the person is now short of seats in that group.
            if (!closed && status.Missing?.FirstOrDefault(m => (m.GroupId ?? "") == group) is { } shortOf)
            {
                var now = await db.Assignments.Where(a => a.BallotId == ballot.Id).Select(a => a.SessionId).ToListAsync();
                var groupName = slot.ToRules().GroupList.FirstOrDefault(g => g.Id == shortOf.GroupId)?.Name;
                await OrganiserMailAsync(db, slot, name, given, AllocationEndpoints.SeatText(activities, now), req.Replacement,
                    (shortOf.Count, groupName), http);
                await db.SaveChangesAsync();
            }
            return Results.Ok(status);
        });

        // Takes a free seat in a group where the person lacks one (also an activity without points, e.g. as a replacement).
        api.MapPost("/slots/{id:guid}/take-seat", async (Guid id, TakeSeatRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            if (slot.Phase != Phase.Published || slot.FrozenAt is null) return Errors("giveBackClosed");
            var ballot = await MyBallotAsync(db, id, name);
            if (ballot is null) return Errors("notVoted");
            var (allocation, activities, _) = await AllocationEndpoints.CurrentAsync(db, slot);
            var me = ballot.ToModel();
            if (allocation.OpenSeats(me, slot.PerParticipant).FirstOrDefault(s => s.Id == req.SessionId) is null) return Errors("seatTaken");

            var seats = await db.Assignments.Where(a => a.BallotId == ballot.Id).Select(a => a.SessionId).ToListAsync();
            db.Assignments.Add(new AssignmentEntity { FairSlotId = id, BallotId = ballot.Id, SessionId = req.SessionId });
            AllocationEndpoints.Log(db, id, name, "tookSeat", name, AllocationEndpoints.SeatText(activities, seats),
                AllocationEndpoints.SeatText(activities, seats.Append(req.SessionId)));
            await db.SaveChangesAsync();
            return Results.Ok(await StatusAsync(db, slot, name));
        });

        // A free seat offered when closed: take it (giving up the seat named in the offer) or leave it to the next person.
        api.MapPost("/slots/{id:guid}/offers/{offerId:guid}/{answer:regex(^(accept|decline)$)}", async (Guid id, Guid offerId, string answer,
            OfferAnswerRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (slot is null || !await Database.CanTakePartAsync(db, id, name)) return Results.NotFound();
            var url = Mailing.BaseUrl(http.Request);
            var error = answer == "accept"
                ? await SeatOffers.AcceptAsync(db, slot, offerId, name, url)
                : await SeatOffers.DeclineAsync(db, slot, offerId, name, url);
            return error is null ? Results.Ok(await StatusAsync(db, slot, name)) : Errors(error);
        });

        api.MapGet("/slots/{id:guid}", async (Guid id, AppDbContext db, IConfiguration cfg) =>
            await ToDto(db, id, cfg) is { } dto ? Results.Ok(dto) : Results.NotFound());

        // In required staffing every date also says how many people are still missing, even without the interim status.
        api.MapGet("/slots/{id:guid}/activities", async (Guid id, AppDbContext db) =>
        {
            var slot = await db.FairSlots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
            if (slot is not { RequiredStaffing: true }) return (await Database.LoadActivitiesAsync(db, id)).Select(ToDto).ToList();
            var (allocation, activities, _) = await AllocationEndpoints.CurrentAsync(db, slot);
            return activities.Select(a => ToDto(a) with
            {
                Sessions = a.Sessions.Select(s => ToDto(s) with { Missing = s.Capacity is { } c ? Math.Max(0, c - allocation.Load(s)) : null }).ToList(),
            }).ToList();
        });

        api.MapPost("/slots/{id:guid}/ballots", async (Guid id, BallotRequest req, AppDbContext db, HttpContext http) =>
        {
            var slot = await db.FairSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            if (slot.Phase != Phase.Open || Phases.DeadlinePassed(slot)) return Errors("registrationClosed");
            if (await ParticipantEndpoints.ResolveNameAsync(http, req.Name) is not { } name) return Results.Unauthorized();
            if (!await Database.CanTakePartAsync(db, id, name)) return Errors("notListed");

            var activities = await Database.LoadActivitiesAsync(db, id);
            var points = (req.Points ?? []).Where(p => p.Value != 0).ToDictionary();
            var unavailable = (req.UnavailableSessions ?? []).ToHashSet();
            var friends = Validation.FriendCode(req.Friends);
            var rules = slot.ToRules();
            var errors = Validation.Check(new Ballot("new", name, points, unavailable, Extra: req.Extra, Friends: friends), rules, activities);
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));
            if (friends is not null && await db.Ballots.CountAsync(b => b.FairSlotId == id && !b.IsSample && b.Friends == friends && b.Name.ToLower() != name.ToLower()) >= rules.Friends)
                return Results.BadRequest(new ErrorsDto([new ValidationError("friendsFull", new Dictionary<string, object> { ["max"] = rules.Friends })]));

            // A new ballot of the same person replaces the previous one.
            var existing = await db.Ballots.Where(b => b.FairSlotId == id && !b.IsSample && b.Name.ToLower() == name.ToLower()).ToListAsync();
            db.Ballots.RemoveRange(existing);
            var entity = new BallotEntity
            {
                FairSlotId = id, Name = name,
                Points = points.Select(p => new BallotPoints { ActivityId = p.Key, Points = p.Value }).ToList(),
                UnavailableSessions = unavailable.ToList(),
                Extra = req.Extra,
                Friends = friends,
                // Keep earlier waiting-list choices for activities that still have points.
                Waitlist = existing.SelectMany(b => b.Waitlist).Where(points.ContainsKey).Distinct().ToList(),
                GiveUp = existing.SelectMany(b => b.GiveUp).Where(x => points.ContainsKey(x.Split('>', 2)[0])).Distinct().ToList(),
            };
            db.Ballots.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = entity.Id, replaced = existing.Count > 0 });
        });
    }

    public static async Task<SlotDto?> ToDto(AppDbContext db, Guid id, IConfiguration cfg)
    {
        var s = await db.FairSlots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (s is null) return null;
        var ballots = await db.Ballots.CountAsync(b => b.FairSlotId == id);
        return new SlotDto(s.Id, s.Name, s.Phase, s.RegistrationDeadline, s.ToRules(), ballots, s.SampleCount, !string.IsNullOrEmpty(cfg["Admin:Key"]), s.OpenToAll, s.Parish, s.Organiser, s.Visibility, s.ShowInterim,
            DeleteOn: Retention.DeleteOn(s.EndedAt, Retention.Days(cfg)), AutoFill: s.AutoFill);
    }

    public static ActivityDto ToDto(Activity a) => new(a.Id, a.Title, a.RestrictedTo, a.Seats,
        a.Sessions.Select(ToDto).ToList(), a.GroupId, a.Description, a.Parish, a.Leader);

    private static SessionDto ToDto(Session s) => new(s.Id, s.Start, s.End, s.Location, s.Capacity, s.Flexible);

    /// <summary>With the leader's e-mail address, which participants do not see.</summary>
    public static ActivityDto ToAdminDto(Activity a) => ToDto(a) with { LeaderEmail = a.LeaderEmail };

    /// <summary>
    /// Uses the valid allocation (stored once frozen, otherwise calculated with the ballots so far). Per activity the person gave points to:
    /// "fixed" (would get it), "waitlist" (all seats taken), "free" (seats left, but the person already has enough higher-ranked
    /// activities, or the date clashes) or "givenBack". The waiting-list position counts people without a seat there who gave more
    /// points; while visible only those who ticked it. While visible it also says what a tick would cost, which seats can be
    /// given back and which seats the person lacks, with the free ones they may take.
    /// </summary>
    private static async Task<MyStatusDto> StatusAsync(AppDbContext db, FairSlotEntity slot, string name)
    {
        var (allocation, activities, entities) = await AllocationEndpoints.CurrentAsync(db, slot);
        var mine = entities.Where(b => !b.IsSample && b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).MaxBy(b => b.SubmittedAt);
        var ballots = entities.Select(b => b.ToModel()).ToList();

        bool Full(Activity a) => a.Sessions.All(s => s.Capacity is { } c && allocation.Load(s) >= c);
        var full = activities.Where(Full).Select(a => a.Id).ToList();
        if (mine is null) return new MyStatusDto(false, [], full);

        var visible = slot.Phase == Phase.Published;
        var canCancel = visible || slot.Phase == Phase.Closed;
        var now = Phases.Now();
        var me = ballots.Single(b => b.Id == mine.Id.ToString());
        var assigned = allocation.Assignments[me.Id].ToDictionary(x => x.Session.ActivityId);
        var titles = activities.ToDictionary(a => a.Id, a => a.Title);
        var list = activities.Where(a => me.PointsFor(a.Id) > 0 || assigned.ContainsKey(a.Id))
            .OrderByDescending(a => me.PointsFor(a.Id))
            .Select(a =>
            {
                if (assigned.TryGetValue(a.Id, out var x))
                    return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), "fixed", null, false,
                        x.Session.Start, x.Session.End, x.Session.Flexible, x.Session.Location, CanGiveBack: canCancel && x.Session.Start > now);
                if (me.HasDeclined(a.Id))
                    return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), "givenBack", null, false, null, null, false, null);
                var waiting = full.Contains(a.Id);
                int? position = waiting
                    ? 1 + ballots.Count(b => b.Id != me.Id && b.PointsFor(a.Id) > me.PointsFor(a.Id) && (!visible || b.Ticked(a.Id)) &&
                        allocation.Assignments[b.Id].All(y => y.Session.ActivityId != a.Id))
                    : null;
                var (canMoveUp, givesUp) = visible ? allocation.MoveUpFor(me, a, slot.PerParticipant) : (false, null);
                var options = visible
                    ? allocation.GiveUpOptions(me, a, slot.PerParticipant).Select(s => new GiveUpOptionDto(s.ActivityId, titles[s.ActivityId], me.PointsFor(s.ActivityId))).ToList()
                    : null;
                return new MyActivityStatusDto(a.Id, a.Title, a.GroupId, me.PointsFor(a.Id), waiting ? "waitlist" : "free", position,
                    mine.Waitlist.Contains(a.Id), null, null, false, null, canMoveUp, givesUp is null ? null : titles[givesUp.ActivityId],
                    GiveUpOptions: options, GiveUpChoice: me.GiveUp?.GetValueOrDefault(a.Id));
            }).ToList();
        IReadOnlyList<MissingDto> missing = !visible ? [] : allocation.MissingSeats(me, slot.PerParticipant).Select(m => new MissingDto(m.Group?.Id, m.Count,
            allocation.OpenSeats(me, slot.PerParticipant)
                .Where(s => allocation.GroupKey(s.ActivityId) == (m.Group?.Id ?? ""))
                .OrderByDescending(s => me.PointsFor(s.ActivityId)).ThenBy(s => s.Start)
                .Select(s => new OpenSeatDto(s.Id, s.ActivityId, titles[s.ActivityId], s.Start, s.End, s.Flexible, s.Location, me.PointsFor(s.ActivityId)))
                .ToList())).ToList();
        return new MyStatusDto(true, list, full, missing, await SeatOffers.MineAsync(db, slot, mine.Id, activities));
    }

    /// <summary>The person's latest own ballot (tracked, with points).</summary>
    private static Task<BallotEntity?> MyBallotAsync(AppDbContext db, Guid slotId, string name) =>
        db.Ballots.Include(b => b.Points)
            .Where(b => b.FairSlotId == slotId && !b.IsSample && b.Name.ToLower() == name.ToLower())
            .OrderByDescending(b => b.SubmittedAt).FirstOrDefaultAsync();

    /// <summary>
    /// Tells the person responsible ("E-Mail für Antworten" of the vote) about a cancellation when closed, with what happens to the
    /// seat (<paramref name="fill"/>: "offered" or why not, null without automatic filling), or, while visible, that a seat given
    /// back left the person short of seats (<paramref name="shortOf"/>).
    /// Replies go to the person, if they have an e-mail address on the list; the mail belongs to them, so admins of their
    /// Pfarrgemeinde see it as well.
    /// </summary>
    private static async Task OrganiserMailAsync(AppDbContext db, FairSlotEntity slot, string name, string seat, string left, bool replacement,
        (int Count, string? Group)? shortOf, HttpContext http, string? fill = null)
    {
        if (string.IsNullOrWhiteSpace(slot.OrganiserEmail)) return;
        var person = await db.Participants.FirstOrDefaultAsync(p => p.FairSlotId == slot.Id && p.Name.ToLower() == name.ToLower());
        var replyTo = person?.Email is { Length: > 0 } email ? email : null;
        var body = new StringBuilder("Hallo,\n\n");
        if (shortOf is { } s)
        {
            body.Append($"{name} hat bei „{slot.Name}“ einen Platz zurückgegeben: {seat}.\n")
                .Append($"Es {(s.Count == 1 ? "fehlt" : "fehlen")} jetzt {s.Count} {(s.Count == 1 ? "Platz" : "Plätze")}{(s.Group is null ? "" : $" in „{s.Group}“")}.\n");
        }
        else body.Append($"{name} hat bei „{slot.Name}“ abgesagt: {seat}.\n");
        body.Append($"Ersatzplatz gewünscht: {(replacement ? "ja" : "nein")}\n")
            .Append($"Plätze jetzt: {(left.Length > 0 ? left : "keine")}\n\n");
        body.Append(shortOf is not null
            ? replacement ? $"Wird bei einer Veranstaltung, der {name} Punkte gegeben hat, ein Platz frei, rückt {name} von selbst nach. " : ""
            : fill switch
            {
                "offered" => "Der Platz wird jetzt automatisch der nächsten Person auf der Warteliste angeboten (siehe „Zuteilung“). " +
                    "Nimmt ihn niemand, bekommst du eine Mail. ",
                "nobodyLeft" => "Niemand wartet auf diesen Platz, daher wird er nicht angeboten. ",
                "offerTooLate" => "Der Termin ist zu nah, um den Platz noch anzubieten. ",
                null => "Die Zuteilung ist abgeschlossen, daher rückt niemand von selbst nach. ",
                _ => "",
            });
        body.Append($"Unter „Zuteilung“ kannst du {name} von Hand einteilen (auch überbuchen)")
            .Append(shortOf is not null || fill == "offered" ? "." : " oder den Platz frei lassen.")
            .Append(person?.Email is { Length: > 0 } ? $" {name} bekommt dann eine Mail mit den neuen Plätzen." : "")
            .Append(replyTo is null ? "\n" : $"\nEine Antwort auf diese Mail geht direkt an {name}, z. B. um eine andere Veranstaltung vorzuschlagen.\n")
            .Append($"{Mailing.BaseUrl(http.Request)}/admin/s/{slot.Id}\n\nUrfair");
        var subject = shortOf is null ? $"Absage: {name} – {slot.Name}" : $"Platz fehlt: {name} – {slot.Name}";
        var mail = new MailEntity { ParticipantId = person?.Id, To = slot.OrganiserEmail, Subject = subject, Body = body.ToString() };
        Mailing.Add(db, slot, mail);
        if (replyTo is not null) mail.ReplyTo = replyTo;
    }

    /// <summary>Interim status during registration only if the organisers switched it on; the allocation once published.</summary>
    private static bool ShowsStatus(FairSlotEntity slot) => slot.Phase == Phase.Open && slot.ShowInterim || Phases.ShowsResult(slot.Phase);

    public static IResult Errors(string code) => Results.BadRequest(new ErrorsDto([new ValidationError(code)]));
}
