using Urfair.Core;

namespace Urfair.Core.Tests;

/// <summary>"More than required": extra seats after the basic round (Ballot.Extra, Rules.MaxExtra).</summary>
public class ExtraSeatsTests
{
	private static readonly DateTime Day = new(2026, 10, 3);

	private static Activity Act(string id, int capacity, int hour, string? group = null) =>
		new(id, id.ToUpperInvariant(), [new Session(id + "1", id, Day.AddHours(hour), Day.AddHours(hour + 1), "", capacity)], GroupId: group);

	private static Ballot Ballot(string id, Dictionary<string, int> points, int extra = 0) =>
		new(id, id, points, new HashSet<string>(), Extra: extra);

	private static string[] Seats(Allocator a, string ballot) => a.Assignments[ballot].Select(x => x.Session.ActivityId).Order().ToArray();

	[Fact]
	public void An_extra_seat_goes_to_a_free_activity_with_points_after_the_basic_round()
	{
		Activity[] acts = [Act("a", 1, 9), Act("b", 2, 11), Act("c", 2, 13)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 }, extra: 2);
		var y = Ballot("y", new() { ["b"] = 100 });

		var alloc = Allocator.PointBased(acts, [x, y], 1, 1);

		// x gets a, then b as an extra seat; c has no points from x, so no second extra seat.
		Assert.Equal(["a", "b"], Seats(alloc, "x"));
		Assert.Equal(["b"], Seats(alloc, "y"));
	}

	[Fact]
	public void Extra_seats_never_take_a_seat_from_the_basic_round()
	{
		// b has one seat: y needs it for the basic round, so x does not get it as an extra seat.
		Activity[] acts = [Act("a", 1, 9), Act("b", 1, 11)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 }, extra: 1);
		var y = Ballot("y", new() { ["b"] = 100 });

		var alloc = Allocator.PointBased(acts, [x, y], 1, 1);

		Assert.Equal(["a"], Seats(alloc, "x"));
		Assert.Equal(["b"], Seats(alloc, "y"));
	}

	[Fact]
	public void More_points_win_an_extra_seat()
	{
		Activity[] acts = [Act("a", 2, 9), Act("b", 1, 11)];
		var x = Ballot("x", new() { ["a"] = 70, ["b"] = 30 }, extra: 1);
		var y = Ballot("y", new() { ["a"] = 60, ["b"] = 40 }, extra: 1);

		var alloc = Allocator.PointBased(acts, [x, y], 1, 1);

		// Both get a in the basic round; the only seat of b goes to y, who gave it more points.
		Assert.Equal(["a"], Seats(alloc, "x"));
		Assert.Equal(["a", "b"], Seats(alloc, "y"));
	}

	[Fact]
	public void Without_extra_nothing_changes()
	{
		Activity[] acts = [Act("a", 2, 9), Act("b", 2, 11)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 });

		Assert.Equal(["a"], Seats(Allocator.PointBased(acts, [x], 1, 1), "x"));
	}

	[Fact]
	public void Group_quotas_do_not_limit_extra_seats()
	{
		ActivityGroup[] groups = [new("g", "Green", 1, 1)];
		Activity[] acts = [Act("a", 2, 9, "g"), Act("b", 2, 11, "g")];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 }, extra: 1);

		var alloc = Allocator.PointBased(acts, [x], 1, 1, groups);

		Assert.Equal(["a", "b"], Seats(alloc, "x"));
	}

	[Fact]
	public void Extra_seats_are_validated_against_the_maximum()
	{
		Activity[] acts = [Act("a", 2, 9), Act("b", 2, 11)];
		var rules = new Rules(PointBudget: 100, MinActivities: 2, MaxPerActivity: 60, PerParticipant: 1, MaxExtra: 1);
		var ok = Ballot("x", new() { ["a"] = 50, ["b"] = 50 }, extra: 1);

		Assert.Empty(Validation.Check(ok, rules, acts));
		Assert.Contains(Validation.Check(ok with { Extra = 2 }, rules, acts), e => e.Code == "extraInvalid");
		Assert.Contains(Validation.Check(ok with { Extra = -1 }, rules, acts), e => e.Code == "extraInvalid");
		Assert.Contains(Validation.Check(rules with { MaxExtra = Validation.MaxExtraSeats + 1 }), e => e.Code == "maxExtraInvalid");
		Assert.Empty(Validation.Check(rules));
	}
}
