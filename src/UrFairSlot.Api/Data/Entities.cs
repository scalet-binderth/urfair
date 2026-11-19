using UrFairSlot.Core;

namespace UrFairSlot.Api.Data;

public class ActivityEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? RestrictedTo { get; set; }
    public int Popularity { get; set; } = 5;
    public int SortOrder { get; set; }
    public List<SessionEntity> Sessions { get; set; } = [];

    public Activity ToModel() => new(Id, Title,
        Sessions.OrderBy(s => s.Start).Select(s => new Session(s.Id, Id, s.Start, s.End, s.Location, s.Capacity, s.Flexible)).ToList(),
        RestrictedTo, Popularity);
}

public class SessionEntity
{
    public string Id { get; set; } = "";
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
    public string Name { get; set; } = "";
    public string? Gender { get; set; }
    public bool IsSample { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public List<BallotPoints> Points { get; set; } = [];
    public List<string> UnavailableSessions { get; set; } = [];

    public Ballot ToModel() => new(Id.ToString(), Name,
        Points.ToDictionary(p => p.ActivityId, p => p.Points),
        UnavailableSessions.ToHashSet(), Gender);
}

/// <summary>Points a ballot gives to one activity (owned by <see cref="BallotEntity"/>).</summary>
public class BallotPoints
{
    public string ActivityId { get; set; } = "";
    public int Points { get; set; }
}

/// <summary>Single-row table with the organiser's settings.</summary>
public class SettingsEntity
{
    public int Id { get; set; } = 1;
    public int PointBudget { get; set; } = 100;
    public int MinActivities { get; set; } = 5;
    public int MaxPerActivity { get; set; } = 40;
    public int PerParticipant { get; set; } = 3;
    public int Seed { get; set; } = 2027;
    public int SampleCount { get; set; } = 140;
    public DateTime? RegistrationDeadline { get; set; }

    public Rules ToRules() => new(PointBudget, MinActivities, MaxPerActivity, PerParticipant);
}
