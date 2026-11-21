namespace Urfair.Core;

/// <summary>
/// Assigns participants to sessions.
/// <see cref="PointBased"/>: round-based, higher points win, ties broken by lottery, followed by Pareto-improving swaps.
/// <see cref="FirstComeFirstServed"/>: baseline for comparison, random arrival order (simulates the rush at opening time).
/// </summary>
public sealed class Allocator
{
    private readonly IReadOnlyList<Activity> _activities;
    private readonly IReadOnlyList<Ballot> _ballots;
    private readonly Dictionary<string, List<Assignment>> _assignments;
    private readonly Dictionary<string, int> _load = new();
    private readonly Dictionary<string, ActivityGroup> _groupOf;

    private Allocator(IReadOnlyList<Activity> activities, IReadOnlyList<Ballot> ballots, IReadOnlyList<ActivityGroup>? groups)
    {
        _activities = activities;
        _ballots = ballots;
        _assignments = ballots.ToDictionary(b => b.Id, _ => new List<Assignment>());
        var byId = (groups ?? []).ToDictionary(g => g.Id);
        _groupOf = activities.Where(a => a.GroupId is not null && byId.ContainsKey(a.GroupId)).ToDictionary(a => a.Id, a => byId[a.GroupId!]);
    }

    public IReadOnlyDictionary<string, List<Assignment>> Assignments => _assignments;
    public int Load(Session s) => _load.GetValueOrDefault(s.Id);

    // ---------- public procedures ----------

    /// <param name="locked">Seats fixed by the organisers; they are given first and stay as they are.</param>
    public static Allocator PointBased(IReadOnlyList<Activity> activities, IReadOnlyList<Ballot> ballots, int perParticipant, int seed,
        IReadOnlyList<ActivityGroup>? groups = null, IEnumerable<FixedAssignment>? locked = null)
    {
        var a = new Allocator(activities, ballots, groups);
        a.Preassign((locked ?? []).Select(x => x with { Locked = true }));
        var rnd = new Random(seed);
        var lottery = ballots.ToDictionary(b => b.Id, _ => rnd.NextDouble());
        var ranking = ballots.ToDictionary(b => b.Id, a.Ranking);

        for (var round = 1; round <= perParticipant; round++)
        {
            var pending = ballots.Where(b => a._assignments[b.Id].Count < round).ToList();
            var withoutBid = new List<Ballot>();
            while (pending.Count > 0)
            {
                var bids = new List<(Ballot Ballot, Activity Activity, int Points)>();
                foreach (var b in pending)
                {
                    var act = ranking[b.Id].FirstOrDefault(x => a.Available(b, x).Any());
                    if (act is null) withoutBid.Add(b);
                    else bids.Add((b, act, b.PointsFor(act.Id)));
                }
                var next = new List<Ballot>();
                foreach (var bid in bids.OrderByDescending(x => x.Points).ThenBy(x => lottery[x.Ballot.Id]))
                {
                    var s = a.Available(bid.Ballot, bid.Activity).OrderByDescending(a.Free).ThenBy(x => x.Start).FirstOrDefault();
                    if (s is null) next.Add(bid.Ballot);
                    else a.Assign(bid.Ballot, s);
                }
                pending = next;
            }
            foreach (var b in withoutBid.OrderBy(b => lottery[b.Id])) a.AssignFallback(b);
        }
        a.ImproveBySwapping();
        return a;
    }

    public static Allocator FirstComeFirstServed(IReadOnlyList<Activity> activities, IReadOnlyList<Ballot> ballots, int perParticipant, int seed, IReadOnlyList<ActivityGroup>? groups = null)
    {
        var a = new Allocator(activities, ballots, groups);
        var rnd = new Random(seed ^ 0x27d4eb2d);
        foreach (var b in ballots.OrderBy(_ => rnd.NextDouble()).ToList())
        {
            foreach (var act in a.Ranking(b))
            {
                if (a._assignments[b.Id].Count >= perParticipant) break;
                var s = a.Available(b, act).OrderBy(x => x.Start).FirstOrDefault();
                if (s is not null) a.Assign(b, s);
            }
            while (a._assignments[b.Id].Count < perParticipant && a.AssignFallback(b)) { }
        }
        return a;
    }

    /// <summary>A stored (frozen) allocation, e.g. after moves by hand; seats of unknown ballots or sessions are skipped.</summary>
    public static Allocator FromAssignments(IReadOnlyList<Activity> activities, IReadOnlyList<Ballot> ballots, IEnumerable<FixedAssignment> assignments,
        IReadOnlyList<ActivityGroup>? groups = null)
    {
        var a = new Allocator(activities, ballots, groups);
        a.Preassign(assignments);
        return a;
    }

    public Metrics Evaluate(int perParticipant)
    {
        var values = new List<double>();
        int top = 0, fallback = 0, incomplete = 0;
        foreach (var b in _ballots)
        {
            var list = _assignments[b.Id];
            values.Add(Satisfaction(b, perParticipant));
            var best = b.Points.Values.DefaultIfEmpty(0).Max();
            if (best > 0 && list.Any(x => x.Points == best)) top++;
            if (list.Any(x => x.IsFallback)) fallback++;
            if (list.Count < perParticipant) incomplete++;
        }
        var n = Math.Max(1, _ballots.Count);
        var tenth = Math.Max(1, (int)Math.Round(n * 0.1));
        return new Metrics(
            _ballots.Count,
            top / (double)n,
            values.DefaultIfEmpty(1).Average(),
            values.Order().Take(tenth).DefaultIfEmpty(1).Average(),
            fallback,
            incomplete);
    }

    /// <summary>Points achieved relative to the best possible (top N of the participant's own ballot, per group if there are groups).</summary>
    public double Satisfaction(Ballot b, int perParticipant)
    {
        var reachable = _activities.Where(x => x.Sessions.Any(s => !b.UnavailableSessions.Contains(s.Id))).ToList();
        int Top(IEnumerable<Activity> list, int n) => list.Select(x => b.PointsFor(x.Id)).OrderDescending().Take(n).Sum();
        var best = _groupOf.Count == 0
            ? Top(reachable, perParticipant)
            : _groupOf.Values.Distinct().Sum(g => Top(reachable.Where(x => GroupOf(x.Id) == g), g.PerParticipant));
        var got = _assignments[b.Id].Sum(x => x.Points);
        return best == 0 ? 1 : Math.Min(1, got / (double)best);
    }

    // ---------- internals ----------

    private List<Activity> Ranking(Ballot b) => _activities
        .Where(x => b.PointsFor(x.Id) > 0)
        .OrderByDescending(x => b.PointsFor(x.Id)).ThenBy(x => x.Popularity)
        .ToList();

    private double Free(Session s) => s.Capacity is { } c ? c - Load(s) : double.PositiveInfinity;

    private bool Fits(Ballot b, Session s, Assignment? except = null)
    {
        if (b.UnavailableSessions.Contains(s.Id)) return false;
        foreach (var x in _assignments[b.Id])
        {
            if (ReferenceEquals(x, except)) continue;
            if (x.Session.ActivityId == s.ActivityId || x.Session.Overlaps(s)) return false;
        }
        // Group quota: e.g. at most 2 green and 1 blue project per participant.
        if (GroupOf(s.ActivityId) is { } g &&
            _assignments[b.Id].Count(x => !ReferenceEquals(x, except) && GroupOf(x.Session.ActivityId) == g) >= g.PerParticipant)
            return false;
        return true;
    }

    private ActivityGroup? GroupOf(string activityId) => _groupOf.GetValueOrDefault(activityId);

    private IEnumerable<Session> Available(Ballot b, Activity act, Assignment? except = null) =>
        act.Sessions.Where(s => Free(s) > 0 && Fits(b, s, except));

    private void Assign(Ballot b, Session s, bool locked = false)
    {
        _assignments[b.Id].Add(new Assignment(b.Id, s, b.PointsFor(s.ActivityId)) { Locked = locked });
        _load[s.Id] = Load(s) + 1;
    }

    private void Preassign(IEnumerable<FixedAssignment> list)
    {
        var sessions = _activities.SelectMany(x => x.Sessions).ToDictionary(s => s.Id);
        var ballots = _ballots.ToDictionary(b => b.Id);
        foreach (var x in list)
            if (ballots.TryGetValue(x.BallotId, out var b) && sessions.TryGetValue(x.SessionId, out var s))
                Assign(b, s, x.Locked);
    }

    private void Move(Assignment x, Session target, Ballot b)
    {
        _load[x.Session.Id] = Load(x.Session) - 1;
        x.Session = target;
        x.Points = b.PointsFor(target.ActivityId);
        _load[target.Id] = Load(target) + 1;
    }

    private double Utilisation(Session s) => s.Capacity is { } c ? Load(s) / (double)c : 0.6;

    /// <summary>Assigns a fallback activity with as much room as possible.</summary>
    private bool AssignFallback(Ballot b)
    {
        var s = _activities
            .Where(x => x.RestrictedTo is null || b.Gender is null || x.RestrictedTo == b.Gender)
            .SelectMany(x => Available(b, x))
            .OrderBy(Utilisation).ThenBy(x => x.Start)
            .FirstOrDefault();
        if (s is null) return false;
        Assign(b, s);
        return true;
    }

    /// <summary>Applies improvements that leave nobody worse off.</summary>
    private void ImproveBySwapping()
    {
        for (var pass = 0; pass < 15; pass++)
        {
            var changed = false;

            // 1. Move into freed-up seats of higher-ranked activities
            foreach (var b in _ballots)
            foreach (var x in _assignments[b.Id].Where(x => !x.Locked))
            {
                foreach (var act in Ranking(b).Where(act => b.PointsFor(act.Id) > x.Points))
                {
                    var s = Available(b, act, x).FirstOrDefault();
                    if (s is null) continue;
                    Move(x, s, b);
                    changed = true;
                    break;
                }
            }

            // 2. Pairwise swaps where neither participant is worse off
            for (var i = 0; i < _ballots.Count; i++)
            for (var j = i + 1; j < _ballots.Count; j++)
            {
                var b1 = _ballots[i];
                var b2 = _ballots[j];
                foreach (var x1 in _assignments[b1.Id])
                foreach (var x2 in _assignments[b2.Id])
                {
                    if (x1.Locked || x2.Locked || x1.Session.ActivityId == x2.Session.ActivityId) continue;
                    var g1 = b1.PointsFor(x2.Session.ActivityId) - x1.Points;
                    var g2 = b2.PointsFor(x1.Session.ActivityId) - x2.Points;
                    if (g1 < 0 || g2 < 0 || g1 + g2 == 0) continue;
                    if (!Fits(b1, x2.Session, x1) || !Fits(b2, x1.Session, x2)) continue;
                    var s1 = x1.Session;
                    var s2 = x2.Session;
                    Move(x1, s2, b1);
                    Move(x2, s1, b2);
                    changed = true;
                }
            }

            if (!changed) break;
        }
    }
}
