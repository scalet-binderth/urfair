using Urfair.Core;

namespace Urfair.Api.Data;

/// <summary>
/// Lifecycle of a FairSlot: draft ? registration (Open) ? allocation, first internal (Allocation), then visible
/// to participants (Published) ? lists final (Closed) ? read only, deletion period starts (Ended).
/// From Allocation on the allocation is frozen. Open ends at the deadline and Ended starts after the last date, both on their own.
/// </summary>
public enum Phase { Draft, Open, Allocation, Published, Closed, Ended }

/// <summary>Who sees and edits a FairSlot in the organisation area (Junia admins always may).</summary>
public enum Visibility
{
    /// <summary>All admins of all Pfarrgemeinden see and edit it.</summary>
    JuniaShared,
    /// <summary>Only admins of the FairSlot's Pfarrgemeinde.</summary>
    ParishOnly,
    /// <summary>All see it; only Junia admins edit it (Pfarrgemeinde admins maintain their own participants).</summary>
    JuniaReadOnly,
}

/// <summary>One allocation round, e.g. "Confirmation preparation 2026/27", with its own rules, activities and ballots.</summary>
public class FairSlotEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Phase Phase { get; set; } = Phase.Draft;
    public DateTime? RegistrationDeadline { get; set; }
    public int PointBudget { get; set; } = 100;
    public int MinActivities { get; set; } = 5;
    public int MaxPerActivity { get; set; } = 40;
    public int PerParticipant { get; set; } = 3;
    public int Seed { get; set; } = 2027;
    public int SampleCount { get; set; }
    /// <summary>Anyone who signs in may see and vote, without being on the participant list (demo playground, later self-registration).</summary>
    public bool OpenToAll { get; set; }
    /// <summary>Participants see their interim status while registration is open. Off by default: it invites tactics.</summary>
    public bool ShowInterim { get; set; }
    /// <summary>Organising parish and person responsible, shown to participants (optional).</summary>
    public string? Parish { get; set; }
    public string? Organiser { get; set; }
    /// <summary>Replies to the app's e-mails go here (optional; only organisers see it).</summary>
    public string? OrganiserEmail { get; set; }
    public Visibility Visibility { get; set; } = Visibility.JuniaShared;
    /// <summary>Set when the allocation is frozen: from then on the stored <see cref="AssignmentEntity"/> rows count, nothing is recalculated on its own.</summary>
    public DateTime? FrozenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ActivityEntity> Activities { get; set; } = [];
    public List<BallotEntity> Ballots { get; set; } = [];
    public List<ParticipantEntity> Participants { get; set; } = [];
    public List<GroupEntity> Groups { get; set; } = [];

    public Rules ToRules() => new(PointBudget, MinActivities, MaxPerActivity, PerParticipant, 5,
        Groups.OrderBy(g => g.SortOrder).Select(g => g.ToModel()).ToList());

    public void Apply(Rules r)
    {
        (PointBudget, MinActivities, MaxPerActivity, PerParticipant) = (r.PointBudget, r.MinActivities, r.MaxPerActivity, r.PerParticipant);
        // Update groups in place (matched by id) so EF Core does not track two rows with the same key.
        var keep = r.GroupList.Select((g, i) =>
        {
            var e = Groups.FirstOrDefault(x => x.Id == g.Id) ?? new GroupEntity { Id = g.Id };
            (e.Name, e.PerParticipant, e.MinActivities, e.Color, e.SortOrder) = (g.Name, g.PerParticipant, g.MinActivities, g.Color, i);
            return e;
        }).ToList();
        Groups.RemoveAll(x => !keep.Contains(x));
        foreach (var e in keep.Where(e => !Groups.Contains(e))) Groups.Add(e);
    }
}

public class ActivityEntity
{
    public string Id { get; set; } = Ids.New();
    public Guid FairSlotId { get; set; }
    public string Title { get; set; } = "";
    public string? RestrictedTo { get; set; }
    public int Popularity { get; set; } = 5;
    public string? GroupId { get; set; }
    public string? Description { get; set; }
    /// <summary>Optional, only when it differs from the FairSlot.</summary>
    public string? Parish { get; set; }
    public string? Leader { get; set; }
    /// <summary>Where the lists for the leader go; only organisers see it.</summary>
    public string? LeaderEmail { get; set; }
    public int SortOrder { get; set; }
    public List<SessionEntity> Sessions { get; set; } = [];

    public Activity ToModel() => new(Id, Title,
        Sessions.OrderBy(s => s.Start).Select(s => new Session(s.Id, Id, s.Start, s.End, s.Location, s.Capacity, s.Flexible)).ToList(),
        RestrictedTo, Popularity, GroupId, Description, Parish, Leader, LeaderEmail);
}

public class SessionEntity
{
    public string Id { get; set; } = Ids.New();
    public string ActivityId { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public string Location { get; set; } = "";
    public int? Capacity { get; set; }
    public bool Flexible { get; set; }
}

public class BallotEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    public string Name { get; set; } = "";
    public string? Gender { get; set; }
    public bool IsSample { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public List<BallotPoints> Points { get; set; } = [];
    public List<string> UnavailableSessions { get; set; } = [];
    /// <summary>Activities the person ticked to move up into when a seat becomes free (while the allocation is visible).</summary>
    public List<string> Waitlist { get; set; } = [];
    /// <summary>Activities the person gave back; they are never allocated to them again.</summary>
    public List<string> Declined { get; set; } = [];
    /// <summary>Groups (id, "" without a group) where a missing seat is not filled automatically: given back without a replacement or set by hand.</summary>
    public List<string> NoReplacement { get; set; } = [];

    public Ballot ToModel() => new(Id.ToString(), Name,
        Points.ToDictionary(p => p.ActivityId, p => p.Points),
        UnavailableSessions.ToHashSet(), Gender, Declined.ToHashSet(), Waitlist.ToHashSet(), NoReplacement.ToHashSet());
}
/// <summary>
/// A person allowed to vote in a FairSlot. Signs in with a personal link (token) and, if a date of birth is stored,
/// confirms it after the click. In demo mode the name alone is enough.
/// </summary>
public class ParticipantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    /// <summary>Optional (e.g. for confirmation); like the date of birth asked after the link click if stored.</summary>
    public DateOnly? BaptismDate { get; set; }
    /// <summary>Official name of the Pfarrgemeinde (see Parishes), optional.</summary>
    public string? Parish { get; set; }
    /// <summary>Secret part of the personal link /p/{token}.</summary>
    public string Token { get; set; } = NewToken();
    public DateTime? InvitedAt { get; set; }

    /// <summary>128 random bits, URL-safe.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// A message prepared by the app (e.g. an invitation with the personal link), kept as an outbox (see <see cref="Mailing"/>).
/// With a mail server set up it is sent in the background; otherwise it is shown in the demo mailbox or copied by the organisers.
/// </summary>
public class MailEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    public Guid? ParticipantId { get; set; }
    public string To { get; set; } = "";
    /// <summary>Replies go to the vote's person responsible.</summary>
    public string? ReplyTo { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>To be sent by the app (a mail server was set up when it was created); otherwise copied by hand.</summary>
    public bool Outbox { get; set; }
    public DateTime? SentAt { get; set; }
    /// <summary>Failed sending attempts; the last error is kept and the next attempt waits longer.</summary>
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>Points a ballot gives to one activity (owned by <see cref="BallotEntity"/>).</summary>
public class BallotPoints
{
    public string ActivityId { get; set; } = "";
    public int Points { get; set; }
}

/// <summary>One seat of a frozen allocation. Deleted together with its FairSlot, ballot or session.</summary>
public class AssignmentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    public Guid BallotId { get; set; }
    public string SessionId { get; set; } = "";
    /// <summary>Fixed by the organisers (e.g. moved by hand): kept when recalculating.</summary>
    public bool Locked { get; set; }

    public FixedAssignment ToModel() => new(BallotId.ToString(), SessionId, Locked);
}

/// <summary>A seat as calculated when the allocation was frozen, so organisers can see and restore it after moving people.</summary>
public class OriginalAssignmentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    public Guid BallotId { get; set; }
    public string SessionId { get; set; } = "";
}

/// <summary>One change to a FairSlot after the draft (who, when, what): allocation, phases, activities, participant list. Deleted with its FairSlot.</summary>
public class AllocationLogEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    /// <summary>Name of the organiser account.</summary>
    public string Actor { get; set; } = "";
    /// <summary>frozen, recalculated, unfrozen, seats, phase, movedUp, givenBack, cancelled, tookSeat, settings, activity…, listed, unlisted, … (texts: log.* in i18n.ts).</summary>
    public string Kind { get; set; } = "";
    /// <summary>For <c>seats</c>: the participant and their seats before and after (activity titles, with date if needed).</summary>
    public string? Person { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}

/// <summary>
/// A save point: seats, fixed marks, waiting-list ticks and seats given back of every ballot right after a log entry
/// that changed seats (see <see cref="SavePoints"/>). Deleted with its FairSlot.
/// </summary>
public class AllocationSnapshotEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    /// <summary>The log entry whose state this is.</summary>
    public Guid LogId { get; set; }
    /// <summary>JSON, one entry per ballot.</summary>
    public string Data { get; set; } = "";
}

public static class Ids
{
    /// <summary>Short random id for activities and sessions.</summary>
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}

/// <summary>A group of activities with its own quota (owned by <see cref="FairSlotEntity"/>).</summary>
public class GroupEntity
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public int PerParticipant { get; set; } = 1;
    public int MinActivities { get; set; } = 1;
    public string Color { get; set; } = "green";
    public int SortOrder { get; set; }

    public ActivityGroup ToModel() => new(Id, Name, PerParticipant, MinActivities, Color);
}

/// <summary>An organiser account. Junia admins may do everything; others work for the listed Pfarrgemeinden.</summary>
public class AccountEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Lower case; used to sign in.</summary>
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsJuniaAdmin { get; set; }
    /// <summary>Official names (see Parishes) this account works for.</summary>
    public List<string> Parishes { get; set; } = [];
    /// <summary>PBKDF2 hash (see Passwords); null until the password has been set via the setup link.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>One-time link to set the password, created by a Junia admin.</summary>
    public string? SetupToken { get; set; }
    public DateTime? SetupTokenExpires { get; set; }
    /// <summary>Demo accounts sign in with a button, never with a password.</summary>
    public bool IsDemo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
