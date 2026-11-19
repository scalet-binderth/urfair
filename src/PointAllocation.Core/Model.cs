namespace PointAllocation.Core;

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
    int Popularity = 5)
{
    public int? Seats => Sessions.Any(s => s.Capacity is null) ? null : Sessions.Sum(s => s.Capacity!.Value);
}

/// <summary>The point distribution a participant submitted.</summary>
public sealed record Ballot(
    string Id,
    string Name,
    IReadOnlyDictionary<string, int> Points,
    IReadOnlySet<string> UnavailableSessions,
    string? Gender = null)
{
    public int PointsFor(string activityId) => Points.GetValueOrDefault(activityId);
}

public sealed record Rules(
    int PointBudget = 100,
    int MinActivities = 5,
    int MaxPerActivity = 40,
    int PerParticipant = 3,
    int Step = 5);

public sealed class Assignment(string ballotId, Session session, int points)
{
    public string BallotId { get; } = ballotId;
    public Session Session { get; internal set; } = session;
    public int Points { get; internal set; } = points;
    /// <summary>Fallback: the participant gave no points to this activity.</summary>
    public bool IsFallback => Points == 0;
}

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
