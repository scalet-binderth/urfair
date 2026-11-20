namespace Urfair.Core;

/// <summary>Rule checks. Errors carry codes (not texts) so the UI can show them in any language.</summary>
public static class Validation
{
    private static ValidationError E(string code, params (string Key, object Value)[] args) =>
        new(code, args.Length == 0 ? null : args.ToDictionary(a => a.Key, a => a.Value));

    public static List<ValidationError> Check(Ballot ballot, Rules r, IReadOnlyList<Activity> activities)
    {
        var errors = new List<ValidationError>();
        var known = activities.ToDictionary(a => a.Id);

        if (string.IsNullOrWhiteSpace(ballot.Name)) errors.Add(E("nameRequired"));

        foreach (var (id, p) in ballot.Points)
        {
            if (!known.ContainsKey(id)) errors.Add(E("unknownActivity", ("id", id)));
            else if (p < 0) errors.Add(E("negativePoints"));
            else if (p % r.Step != 0) errors.Add(E("pointStep", ("step", r.Step)));
            else if (p > r.MaxPerActivity) errors.Add(E("maxPerActivity", ("max", r.MaxPerActivity), ("activity", known[id].Title)));
        }

        var sum = ballot.Points.Values.Sum();
        if (sum != r.PointBudget) errors.Add(E("budgetNotSpent", ("budget", r.PointBudget), ("sum", sum)));

        var count = ballot.Points.Count(p => p.Value > 0);
        if (count < r.MinActivities) errors.Add(E("tooFewActivities", ("min", r.MinActivities), ("count", count)));

        foreach (var g in r.GroupList)
        {
            var inGroup = ballot.Points.Count(p => p.Value > 0 && known.TryGetValue(p.Key, out var a) && a.GroupId == g.Id);
            if (inGroup < g.MinActivities) errors.Add(E("tooFewInGroup", ("group", g.Name), ("min", g.MinActivities), ("count", inGroup)));
        }

        foreach (var (id, p) in ballot.Points)
        {
            if (p > 0 && known.TryGetValue(id, out var a) && a.Sessions.All(s => ballot.UnavailableSessions.Contains(s.Id)))
                errors.Add(E("noSessionAvailable", ("activity", a.Title)));
        }
        return errors;
    }

    public static List<ValidationError> Check(Activity a)
    {
        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(a.Title)) errors.Add(E("titleRequired"));
        if (a.RestrictedTo is not (null or "m" or "w")) errors.Add(E("restrictionInvalid"));
        if (a.Sessions.Count == 0) errors.Add(E("sessionRequired"));
        foreach (var s in a.Sessions)
        {
            if (s.Capacity is < 1) errors.Add(E("capacityInvalid"));
            if (!s.Flexible && s.End is null) errors.Add(E("endRequired"));
            if (s.End is { } end && end <= s.Start) errors.Add(E("endBeforeStart"));
        }
        return errors.Distinct().ToList();
    }

    public static List<ValidationError> Check(Rules r)
    {
        var errors = new List<ValidationError>();
        if (r.PointBudget % r.Step != 0 || r.MaxPerActivity % r.Step != 0) errors.Add(E("pointStep", ("step", r.Step)));
        if (r.MaxPerActivity > r.PointBudget) errors.Add(E("maxAboveBudget"));
        if (r.MinActivities * r.Step > r.PointBudget) errors.Add(E("tooManyActivitiesForBudget", ("max", r.PointBudget / r.Step)));
        if (r.PerParticipant > r.MinActivities) errors.Add(E("perParticipantAboveMin"));
        if (r.PerParticipant < 1) errors.Add(E("perParticipantTooLow"));
        if (r.GroupList.Count > 0)
        {
            if (r.GroupList.Any(g => string.IsNullOrWhiteSpace(g.Name))) errors.Add(E("groupNameRequired"));
            if (r.GroupList.Any(g => g.PerParticipant < 1 || g.MinActivities < g.PerParticipant)) errors.Add(E("groupMinBelowQuota"));
            if (r.GroupList.Sum(g => g.PerParticipant) != r.PerParticipant) errors.Add(E("groupQuotaSum", ("k", r.PerParticipant)));        }
        return errors;
    }
}
