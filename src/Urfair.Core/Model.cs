namespace Urfair.Core;

/// <summary>A concrete date of an activity. Capacity null = unlimited.</summary>
public sealed record Session(
    string Id,
    string ActivityId,
    DateTime Start,
    DateTime? End,
    string Location,
    int? Capacity,
    bool Flexible = false)
{
    /// <summary>Flexible sessions (date by arrangement) never collide.</summary>
    public bool Overlaps(Session other) =>
        !Flexible && !other.Flexible && End is { } e1 && other.End is { } e2 &&
        Start < e2 && other.Start < e1;
}

/// <summary>An activity of the confirmation preparation, possibly offered at several sessions.</summary>
public sealed record Activity(
    string Id,
    string Title,
    IReadOnlyList<Session> Sessions,
    string? RestrictedTo = null,
    int Popularity = 5,
    string? GroupId = null,
    string? Description = null,
    string? Parish = null,
    string? Leader = null,
    string? LeaderEmail = null)
{
    public int? Seats => Sessions.Any(s => s.Capacity is null) ? null : Sessions.Sum(s => s.Capacity!.Value);
}

/// <summary>
/// The point distribution a participant submitted. For moving up after the allocation: <paramref name="Declined"/> activities
/// were given back and are never allocated again, <paramref name="Waitlist"/> activities are ticked to move up into, and
/// <paramref name="NoReplacement"/> holds the groups (id, "" without a group) where a missing seat is not filled on its own.
/// <paramref name="GiveUp"/> maps a ticked activity to the activity whose seat the person chose to give up for it
/// (otherwise the weakest seat of the group goes). <paramref name="Extra"/>: "more than required", the person would also
/// take up to this many seats more (at most <see cref="Rules.MaxExtra"/>).
/// </summary>
public sealed record Ballot(
    string Id,
    string Name,
    IReadOnlyDictionary<string, int> Points,
    IReadOnlySet<string> UnavailableSessions,
    string? Gender = null,
    IReadOnlySet<string>? Declined = null,
    IReadOnlySet<string>? Waitlist = null,
    IReadOnlySet<string>? NoReplacement = null,
    IReadOnlyDictionary<string, string>? GiveUp = null,
    int Extra = 0)
{
    public int PointsFor(string activityId) => Points.GetValueOrDefault(activityId);
    public bool HasDeclined(string activityId) => Declined?.Contains(activityId) == true;
    public bool Ticked(string activityId) => Waitlist?.Contains(activityId) == true;
    public bool WantsReplacement(string groupKey) => NoReplacement?.Contains(groupKey) != true;
}

/// <summary>
/// A group of activities, e.g. the "green" and "blue" projects of the confirmation preparation.
/// Every participant gets exactly <see cref="PerParticipant"/> activities from the group and must give
/// points to at least <see cref="MinActivities"/> of them.
/// </summary>
public sealed record ActivityGroup(string Id, string Name, int PerParticipant, int MinActivities, string Color = "green");

/// <summary>
/// Rules of a FairSlot. With groups, <see cref="PerParticipant"/> must be the sum of the group quotas.
/// <see cref="MaxExtra"/>: "more than required", participants may offer up to this many seats more (0 = off).
/// <see cref="RequiredStaffing"/>: every date with a seat count must be filled; participants see how many people are
/// still missing while voting.
/// </summary>
public record Rules(
    int PointBudget = 100,
    int MinActivities = 5,
    int MaxPerActivity = 40,
    int PerParticipant = 3,
    int Step = 5,
    IReadOnlyList<ActivityGroup>? Groups = null,
    int MaxExtra = 0,
    bool RequiredStaffing = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ActivityGroup> GroupList => Groups ?? [];

    public ActivityGroup? GroupOf(Activity a) => a.GroupId is null ? null : GroupList.FirstOrDefault(g => g.Id == a.GroupId);

    // Compare groups by content so that "rules changed" detection works.
    public virtual bool Equals(Rules? other) =>
        other is not null && (PointBudget, MinActivities, MaxPerActivity, PerParticipant, Step, MaxExtra, RequiredStaffing) ==
        (other.PointBudget, other.MinActivities, other.MaxPerActivity, other.PerParticipant, other.Step, other.MaxExtra, other.RequiredStaffing) &&
        GroupList.SequenceEqual(other.GroupList);

    public override int GetHashCode() => HashCode.Combine(PointBudget, MinActivities, MaxPerActivity, PerParticipant, Step, GroupList.Count, MaxExtra, RequiredStaffing);
}

public sealed class Assignment(string ballotId, Session session, int points)
{
    public string BallotId { get; } = ballotId;
    public Session Session { get; internal set; } = session;
    public int Points { get; internal set; } = points;
    /// <summary>Fixed by the organisers: kept when recalculating and never swapped.</summary>
    public bool Locked { get; internal init; }
    /// <summary>Fallback: the participant gave no points to this activity.</summary>
    public bool IsFallback => Points == 0;
}

/// <summary>A stored seat of a frozen allocation (or one fixed by hand when recalculating).</summary>
public sealed record FixedAssignment(string BallotId, string SessionId, bool Locked = false);

/// <summary>One step of moving up: the person got <paramref name="Session"/> and gave up <paramref name="GaveUp"/> (null if they lacked a seat).</summary>
public sealed record MoveUp(string BallotId, Session Session, Session? GaveUp, bool Need);

public sealed record Metrics(
    int Count,
    double GotTopChoice,
    double AverageSatisfaction,
    double WorstTenPercentSatisfaction,
    int WithFallback,
    int Incomplete);

/// <summary>Validation problem with a stable code so the UI can translate it.</summary>
public sealed record ValidationError(string Code, IReadOnlyDictionary<string, object>? Args = null)
{
    public override string ToString() =>
        Args is null ? Code : $"{Code}({string.Join(", ", Args.Select(a => $"{a.Key}={a.Value}"))})";
}
