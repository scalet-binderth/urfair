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

    // ---------- moving up (frozen allocation) ----------

    /// <summary>
    /// Fills free seats step by step. Need before wish: first people missing a seat in the activity's group (unless they
    /// declined a replacement) who gave the activity points, then people who ticked it on the waiting list; they give up
    /// their weakest unfixed seat of the same group if it has fewer points (nothing if they lack a seat there). Within each
    /// kind more points win, ties by lottery. A seat given up is free again for the next step. Every step adds a seat or
    /// raises someone's points and nobody loses, so it ends.
    /// </summary>
    public List<MoveUp> FillFreeSeats(int perParticipant, int seed)
    {
        var lottery = Lottery(seed);
        var sessions = _activities.SelectMany(a => a.Sessions).ToList();
        var moves = new List<MoveUp>();
        for (var step = 0; step < 100_000; step++)
        {
            (Ballot Ballot, Session Session, Assignment? GiveUp, bool Need, int Points)? best = null;
            foreach (var s in sessions.Where(s => Free(s) > 0))
            foreach (var b in _ballots)
            {
                var (possible, giveUp) = Exchange(b, s, perParticipant);
                if (!possible) continue;
                var need = giveUp is null && b.WantsReplacement(GroupKey(s.ActivityId));
                if (!need && !b.Ticked(s.ActivityId)) continue;
                var points = b.PointsFor(s.ActivityId);
                if (best is { } x && (x.Need, x.Points, -lottery[x.Ballot.Id]).CompareTo((need, points, -lottery[b.Id])) >= 0) continue;
                best = (b, s, giveUp, need, points);
            }
            if (best is not { } m) break;
            if (m.GiveUp is { } old)
            {
                _assignments[m.Ballot.Id].Remove(old);
                _load[old.Session.Id] = Load(old.Session) - 1;
            }
            Assign(m.Ballot, m.Session);
            moves.Add(new MoveUp(m.Ballot.Id, m.Session, m.GiveUp?.Session, m.Need));
        }
        return moves;
    }

    /// <summary>
    /// Who is next for a free seat in this session, in the order of <see cref="FillFreeSeats"/> (need before tick, more
    /// points, then the lottery), leaving out <paramref name="skip"/>. Null if nobody fits.
    /// </summary>
    public MoveUp? NextFor(Session s, int perParticipant, int seed, Func<Ballot, bool> skip)
    {
        var lottery = Lottery(seed);
        (Ballot Ballot, Assignment? GiveUp, bool Need, int Points)? best = null;
        foreach (var b in _ballots.Where(b => !skip(b)))
        {
            var (possible, giveUp) = Exchange(b, s, perParticipant);
            if (!possible) continue;
            var need = giveUp is null && b.WantsReplacement(GroupKey(s.ActivityId));
            if (!need && !b.Ticked(s.ActivityId)) continue;
            var points = b.PointsFor(s.ActivityId);
            if (best is { } x && (x.Need, x.Points, -lottery[x.Ballot.Id]).CompareTo((need, points, -lottery[b.Id])) >= 0) continue;
            best = (b, giveUp, need, points);
        }
        return best is { } m ? new MoveUp(m.Ballot.Id, s, m.GiveUp?.Session, m.Need) : null;
    }

    /// <summary>Whether the person could move into this session (ignoring free seats), and which seat of theirs would go for it.</summary>
    public (bool Possible, Session? GivesUp) ExchangeFor(Ballot b, Session s, int perParticipant)
    {
        var (possible, giveUp) = Exchange(b, s, perParticipant);
        return (possible, giveUp?.Session);
    }

    /// <summary>
    /// Whether organisers could add this session to a person short of a seat in its group: no clash, not the same
    /// activity twice, time for it and room in the group. Ignores points, seats given back and free seats.
    /// </summary>
    public bool CanAdd(Ballot b, Session s, int perParticipant) =>
        !b.UnavailableSessions.Contains(s.Id) && SameGroup(b, s.ActivityId).Count < Quota(s.ActivityId, perParticipant) &&
        _assignments[b.Id].All(x => x.Session.ActivityId != s.ActivityId && !x.Session.Overlaps(s));

    /// <summary>
    /// Whether the person could move into a session of this activity if a seat were free, and which seat of theirs
    /// would go for it (null if they lack a seat in the group).
    /// </summary>
    public (bool Possible, Session? GivesUp) MoveUpFor(Ballot b, Activity activity, int perParticipant)
    {
        foreach (var s in activity.Sessions)
        {
            var (possible, giveUp) = Exchange(b, s, perParticipant);
            if (possible) return (true, giveUp?.Session);
        }
        return (false, null);
    }

    /// <summary>Missing seats per group (null = without groups) compared with what everyone gets.</summary>
    public IReadOnlyList<(ActivityGroup? Group, int Count)> MissingSeats(Ballot b, int perParticipant) =>
        _groupOf.Count == 0
            ? _assignments[b.Id].Count < perParticipant ? [(null, perParticipant - _assignments[b.Id].Count)] : []
            : _groupOf.Values.Distinct()
                .Select(g => ((ActivityGroup?)g, g.PerParticipant - _assignments[b.Id].Count(x => GroupOf(x.Session.ActivityId) == g)))
                .Where(x => x.Item2 > 0).ToList();

    /// <summary>Free sessions a person missing a seat in that group may take (also without points), not given back, no clash.</summary>
    public IEnumerable<Session> OpenSeats(Ballot b, int perParticipant) =>
        _activities.Where(a => a.RestrictedTo is null || b.Gender is null || a.RestrictedTo == b.Gender)
            .SelectMany(a => a.Sessions)
            .Where(s => Free(s) > 0 && SameGroup(b, s.ActivityId).Count < Quota(s.ActivityId, perParticipant) && Fits(b, s));

    /// <summary>Adds a seat (e.g. a free one the person took).</summary>
    public void Add(string ballotId, Session s) => Assign(_ballots.Single(b => b.Id == ballotId), s);

    /// <summary>Group of an activity for moving up: its id, or "" without a group.</summary>
    public string GroupKey(string activityId) => GroupOf(activityId)?.Id ?? "";

    private List<Assignment> SameGroup(Ballot b, string activityId) =>
        _assignments[b.Id].Where(x => GroupKey(x.Session.ActivityId) == GroupKey(activityId)).ToList();

    /// <summary>Seats per person in the activity's group (without groups: all seats).</summary>
    private int Quota(string activityId, int perParticipant) =>
        _groupOf.Count == 0 ? perParticipant
        : GroupOf(activityId)?.PerParticipant ?? Math.Max(0, perParticipant - _groupOf.Values.Distinct().Sum(g => g.PerParticipant));

    /// <summary>
    /// Seats the person could give up to move into this activity (same group, fewer points, not fixed, no clash), the
    /// pre-selected weakest first. Empty if they lack a seat in the group (nothing to give up) or cannot move there.
    /// </summary>
    public IReadOnlyList<Session> GiveUpOptions(Ballot b, Activity activity, int perParticipant)
    {
        var points = b.PointsFor(activity.Id);
        var same = SameGroup(b, activity.Id);
        if (points <= 0 || same.Count < Quota(activity.Id, perParticipant)) return [];
        return Swappable(b, same, points, activity.Sessions).Select(x => x.Session).ToList();
    }

    /// <summary>
    /// Moving into a session: only with points, not given back and without a clash. With a full group a seat of it with
    /// fewer points goes (the one the person chose, else the weakest; never a fixed one).
    /// </summary>
    private (bool Possible, Assignment? GiveUp) Exchange(Ballot b, Session s, int perParticipant)
    {
        var points = b.PointsFor(s.ActivityId);
        if (points <= 0) return (false, null);
        var same = SameGroup(b, s.ActivityId);
        if (same.Count < Quota(s.ActivityId, perParticipant)) return (Fits(b, s), null);
        var options = Swappable(b, same, points, [s]);
        // A chosen seat still held is binding: if it cannot go for this session, the person does not move here.
        if (b.GiveUp?.GetValueOrDefault(s.ActivityId) is { } chosen && same.Any(x => x.Session.ActivityId == chosen))
        {
            var pick = options.FirstOrDefault(x => x.Session.ActivityId == chosen);
            return (pick is not null, pick);
        }
        var giveUp = options.FirstOrDefault();
        return (giveUp is not null, giveUp);
    }

    /// <summary>Seats of the group that may go for one of the sessions, weakest first (ties: the later one).</summary>
    private List<Assignment> Swappable(Ballot b, List<Assignment> same, int points, IEnumerable<Session> sessions) =>
        same.Where(x => !x.Locked && x.Points < points && sessions.Any(s => Fits(b, s, x)))
            .OrderBy(x => x.Points).ThenByDescending(x => x.Session.Start).ToList();

    // ---------- internals ----------

    private Dictionary<string, double> Lottery(int seed)
    {
        var rnd = new Random(seed);
        return _ballots.ToDictionary(b => b.Id, _ => rnd.NextDouble());
    }

    private List<Activity> Ranking(Ballot b) => _activities
        .Where(x => b.PointsFor(x.Id) > 0)
        .OrderByDescending(x => b.PointsFor(x.Id)).ThenBy(x => x.Popularity)
        .ToList();

    private double Free(Session s) => s.Capacity is { } c ? c - Load(s) : double.PositiveInfinity;

    private bool Fits(Ballot b, Session s, Assignment? except = null)
    {
        if (b.UnavailableSessions.Contains(s.Id) || b.HasDeclined(s.ActivityId)) return false;
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
