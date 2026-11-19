using Urfair.Core;

namespace Urfair.Core.Tests;

public class AllocatorTests
{
    private static readonly Rules Default = new();
    private static readonly IReadOnlyList<Activity> Activities = SampleData.Activities();

    private static Allocator Run(int count = 140, int seed = 2027)
    {
        var ballots = SampleData.Participants(count, Default, Activities, seed);
        return Allocator.PointBased(Activities, ballots, Default.PerParticipant, seed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2027)]
    [InlineData(4711)]
    public void No_session_is_overbooked(int seed)
    {
        var a = Run(160, seed);
        foreach (var s in Activities.SelectMany(x => x.Sessions).Where(s => s.Capacity is not null))
            Assert.True(a.Load(s) <= s.Capacity, $"{s.Id}: {a.Load(s)} > {s.Capacity}");
    }

    [Fact]
    public void Everyone_gets_three_distinct_non_overlapping_activities()
    {
        var a = Run();
        foreach (var (_, list) in a.Assignments)
        {
            Assert.Equal(Default.PerParticipant, list.Count);
            Assert.Equal(list.Count, list.Select(x => x.Session.ActivityId).Distinct().Count());
            foreach (var x in list)
            foreach (var y in list.Where(y => !ReferenceEquals(x, y)))
                Assert.False(x.Session.Overlaps(y.Session), $"{x.Session.Id} / {y.Session.Id}");
        }
    }

    [Fact]
    public void Unavailable_sessions_are_never_assigned()
    {
        var ballots = SampleData.Participants(140, Default, Activities, 2027);
        var a = Allocator.PointBased(Activities, ballots, 3, 2027);
        foreach (var b in ballots)
            Assert.DoesNotContain(a.Assignments[b.Id], x => b.UnavailableSessions.Contains(x.Session.Id));
    }

    [Fact]
    public void Point_based_is_fairer_than_first_come_first_served()
    {
        var ballots = SampleData.Participants(140, Default, Activities, 2027);
        var p = Allocator.PointBased(Activities, ballots, 3, 2027).Evaluate(3);
        var f = Allocator.FirstComeFirstServed(Activities, ballots, 3, 2027).Evaluate(3);
        Assert.True(p.WorstTenPercentSatisfaction > f.WorstTenPercentSatisfaction);
        Assert.True(p.AverageSatisfaction >= f.AverageSatisfaction);
    }

    [Fact]
    public void More_points_take_priority()
    {
        var scarce = new Activity("a", "A", [new Session("a#0", "a", new DateTime(2027, 1, 1, 10, 0, 0), new DateTime(2027, 1, 1, 12, 0, 0), "X", 1)]);
        var open = new Activity("b", "B", [new Session("b#0", "b", new DateTime(2027, 2, 1, 10, 0, 0), new DateTime(2027, 2, 1, 12, 0, 0), "Y", null)]);
        var low = new Ballot("low", "Low", new Dictionary<string, int> { ["a"] = 10, ["b"] = 90 }, new HashSet<string>());
        var high = new Ballot("high", "High", new Dictionary<string, int> { ["a"] = 40, ["b"] = 60 }, new HashSet<string>());
        // Both rank "b" first; in round 2 they compete for the single seat in "a".
        var result = Allocator.PointBased([scarce, open], [low, high], 2, 1);
        Assert.Contains(result.Assignments["high"], x => x.Session.ActivityId == "a");
        Assert.DoesNotContain(result.Assignments["low"], x => x.Session.ActivityId == "a");
    }

    [Fact]
    public void Same_seed_gives_same_result()
    {
        Assert.Equal(Run(100, 99).Evaluate(3), Run(100, 99).Evaluate(3));
    }

    [Fact]
    public void Sample_participants_follow_the_rules()
    {
        foreach (var b in SampleData.Participants(200, Default, Activities, 5))
            Assert.Empty(Validation.Check(b, Default, Activities));
    }

    [Fact]
    public void Demo_fair_slots_have_valid_rules_and_ballots()
    {
        foreach (var (rules, activities) in new[] {
            (DemoData.ExcursionRules, DemoData.Excursion()), (DemoData.HolidayRules, DemoData.HolidayProgramme()), (DemoData.CampRules, DemoData.Camp()) })
        {
            Assert.Empty(Validation.Check(rules));
            Assert.All(activities, a => Assert.Empty(Validation.Check(a)));
            foreach (var b in SampleData.Participants(50, rules, activities, 7))
                Assert.Empty(Validation.Check(b, rules, activities));
        }
    }

    [Fact]
    public void Invalid_activity_is_rejected_with_codes()
    {
        var start = new DateTime(2027, 3, 1, 18, 0, 0);
        var a = new Activity("x", " ", [new Session("s", "x", start, start.AddHours(-1), "Hall", 0)], "x");
        var codes = Validation.Check(a).Select(e => e.Code).ToList();
        Assert.Equal(["titleRequired", "restrictionInvalid", "capacityInvalid", "endBeforeStart"], codes);
        Assert.Contains("sessionRequired", Validation.Check(new Activity("y", "Y", [])).Select(e => e.Code));
    }

    [Fact]
    public void Invalid_ballot_is_rejected_with_codes()
    {
        var b = new Ballot("x", "Test", new Dictionary<string, int> { ["kino"] = 50, ["nacht"] = 30 }, new HashSet<string>());
        var codes = Validation.Check(b, Default, Activities).Select(e => e.Code).ToList();
        Assert.Contains("maxPerActivity", codes);
        Assert.Contains("budgetNotSpent", codes);
        Assert.Contains("tooFewActivities", codes);
    }
}
