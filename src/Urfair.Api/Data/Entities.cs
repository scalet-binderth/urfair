using Urfair.Core;

namespace Urfair.Api.Data;

/// <summary>Lifecycle of a FairSlot.</summary>
public enum Phase { Draft, Open, Closed, Published }

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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ActivityEntity> Activities { get; set; } = [];
    public List<BallotEntity> Ballots { get; set; } = [];
    public List<ParticipantEntity> Participants { get; set; } = [];

    public Rules ToRules() => new(PointBudget, MinActivities, MaxPerActivity, PerParticipant);

    public void Apply(Rules r) =>
        (PointBudget, MinActivities, MaxPerActivity, PerParticipant) = (r.PointBudget, r.MinActivities, r.MaxPerActivity, r.PerParticipant);
}

public class ActivityEntity
{
    public string Id { get; set; } = Ids.New();
    public Guid FairSlotId { get; set; }
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

    public Ballot ToModel() => new(Id.ToString(), Name,
        Points.ToDictionary(p => p.ActivityId, p => p.Points),
        UnavailableSessions.ToHashSet(), Gender);
}

/// <summary>A person allowed to vote in a FairSlot. Identified by name in the demo; e-mail and personal links follow.</summary>
public class ParticipantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FairSlotId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Points a ballot gives to one activity (owned by <see cref="BallotEntity"/>).</summary>
public class BallotPoints
{
    public string ActivityId { get; set; } = "";
    public int Points { get; set; }
}

/// <summary>Single row recording the schema version (see <see cref="Database"/>).</summary>
public class MetaEntity
{
    public int Id { get; set; } = 1;
    public int SchemaVersion { get; set; }
}

public static class Ids
{
    /// <summary>Short random id for activities and sessions.</summary>
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
