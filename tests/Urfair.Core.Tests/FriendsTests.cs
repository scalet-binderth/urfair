using Urfair.Core;

namespace Urfair.Core.Tests;

/// <summary>"Together with friends": friend groups share an activity after the allocation if nobody else loses (Ballot.Friends).</summary>
public class FriendsTests
{
	private static readonly DateTime Day = new(2026, 10, 3);

	private static Activity Act(string id, int capacity, int hour) =>
		new(id, id.ToUpperInvariant(), [new Session(id + "1", id, Day.AddHours(hour), Day.AddHours(hour + 1), "", capacity)]);

	private static Ballot Ballot(string id, Dictionary<string, int> points, string? friends = null) =>
		new(id, id, points, new HashSet<string>(), Friends: friends);

	private static string Seat(Allocator a, string ballot) => a.Assignments[ballot].Single().Session.ActivityId;

	[Fact]
	public void Friends_move_into_a_free_seat_of_an_activity_both_want()
	{
		Activity[] acts = [Act("a", 2, 9), Act("b", 2, 11)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 }, "ab12");
		var y = Ballot("y", new() { ["b"] = 60, ["a"] = 40 }, "AB12");

		var alloc = Allocator.PointBased(acts, [x, y], 1, 1);

		Assert.Equal(Seat(alloc, "x"), Seat(alloc, "y"));
	}

	[Fact]
	public void Without_a_code_or_alone_nothing_changes()
	{
		Activity[] acts = [Act("a", 2, 9), Act("b", 2, 11)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 }, "solo");
		var y = Ballot("y", new() { ["b"] = 60, ["a"] = 40 });

		var alloc = Allocator.PointBased(acts, [x, y], 1, 1);

		Assert.Equal(("a", "b"), (Seat(alloc, "x"), Seat(alloc, "y")));
	}

	[Fact]
	public void Someone_else_swaps_only_without_losing_points()
	{
		Activity[] acts = [Act("a", 2, 9), Act("b", 1, 11)];
		var x = Ballot("x", new() { ["a"] = 100 }, "team");
		var y = Ballot("y", new() { ["b"] = 60, ["a"] = 40 }, "team");

		// z gives a and b the same points: swapping costs z nothing, so y joins x in a and z gets b.
		var even = Allocator.PointBased(acts, [x, y, Ballot("z", new() { ["a"] = 50, ["b"] = 50 })], 1, 1);
		Assert.Equal(("a", "a", "b"), (Seat(even, "x"), Seat(even, "y"), Seat(even, "z")));
		Assert.Equal(50, even.Assignments["z"].Single().Points);

		// z prefers a: z would lose, so the friends stay apart.
		var apart = Allocator.PointBased(acts, [x, y, Ballot("z", new() { ["a"] = 60, ["b"] = 40 })], 1, 1);
		Assert.Equal(("a", "b", "a"), (Seat(apart, "x"), Seat(apart, "y"), Seat(apart, "z")));
	}

	[Fact]
	public void Only_activities_every_member_gave_points_to_count()
	{
		Activity[] acts = [Act("a", 3, 9), Act("b", 3, 11)];
		var x = Ballot("x", new() { ["a"] = 100 }, "team");
		var y = Ballot("y", new() { ["b"] = 100 }, "team");

		var alloc = Allocator.PointBased(acts, [x, y], 1, 1);

		Assert.Equal(("a", "b"), (Seat(alloc, "x"), Seat(alloc, "y")));
	}

	[Fact]
	public void Codes_and_group_sizes_are_validated()
	{
		Activity[] acts = [Act("a", 2, 9), Act("b", 2, 11)];
		var rules = new Rules(PointBudget: 100, MinActivities: 2, MaxPerActivity: 60, PerParticipant: 1, Friends: 3);
		var ok = Ballot("x", new() { ["a"] = 50, ["b"] = 50 }, " ab12 ");

		Assert.Empty(Validation.Check(ok, rules, acts));
		Assert.Contains(Validation.Check(ok, rules with { Friends = 0 }, acts), e => e.Code == "friendsOff");
		Assert.Contains(Validation.Check(ok with { Friends = "a-b" }, rules, acts), e => e.Code == "friendCodeInvalid");
		Assert.Contains(Validation.Check(rules with { Friends = 1 }), e => e.Code == "friendsInvalid");
		Assert.Contains(Validation.Check(rules with { Friends = Validation.MaxFriends + 1 }), e => e.Code == "friendsInvalid");
		Assert.Empty(Validation.Check(rules));
	}
}
