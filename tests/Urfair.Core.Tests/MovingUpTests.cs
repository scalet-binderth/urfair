using Urfair.Core;

namespace Urfair.Core.Tests;

public class MovingUpTests
{
	private static readonly DateTime Day = new(2026, 10, 3);

	private static Activity Act(string id, int capacity, string? group = null, int hour = 9) =>
		new(id, id.ToUpperInvariant(), [new Session(id + "1", id, Day.AddHours(hour), Day.AddHours(hour + 1), "", capacity)], GroupId: group);

	private static Ballot Ballot(string id, Dictionary<string, int> points, string[]? waitlist = null, string[]? declined = null, string[]? noReplacement = null) =>
		new(id, id, points, new HashSet<string>(), null,
			declined is null ? null : new HashSet<string>(declined),
			waitlist is null ? null : new HashSet<string>(waitlist),
			noReplacement is null ? null : new HashSet<string>(noReplacement));

	private static Allocator Frozen(IReadOnlyList<Activity> activities, IReadOnlyList<Ballot> ballots, params (string Ballot, string Activity, bool Locked)[] seats) =>
		Allocator.FromAssignments(activities, ballots, seats.Select(s => new FixedAssignment(s.Ballot, s.Activity + "1", s.Locked)));

	private static Allocator FrozenWithGroups(IReadOnlyList<Activity> activities, IReadOnlyList<Ballot> ballots, IReadOnlyList<ActivityGroup> groups,
		params (string Ballot, string Activity)[] seats) =>
		Allocator.FromAssignments(activities, ballots, seats.Select(s => new FixedAssignment(s.Ballot, s.Activity + "1")), groups);

	private static string[] Seats(Allocator a, string ballot) => a.Assignments[ballot].Select(x => x.Session.ActivityId).Order().ToArray();

	[Fact]
	public void Need_comes_before_a_wish_with_more_points()
	{
		// a is free; x lacks a seat (need), y ticked a with more points but has b.
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 5, hour: 12)];
		var x = Ballot("x", new() { ["a"] = 10 });
		var y = Ballot("y", new() { ["a"] = 90, ["b"] = 10 }, waitlist: ["a"]);
		var alloc = Frozen(acts, [x, y], ("y", "b", false));

		var moves = alloc.FillFreeSeats(1, 1);

		Assert.Equal(["a"], Seats(alloc, "x"));
		Assert.Equal(["b"], Seats(alloc, "y"));
		Assert.True(Assert.Single(moves).Need);
	}

	[Fact]
	public void A_ticked_wish_moves_up_and_the_seat_given_up_fills_on_in_a_chain()
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 1, hour: 12), Act("c", 1, hour: 15)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 }, waitlist: ["a"]);
		var y = Ballot("y", new() { ["b"] = 70, ["c"] = 30 }, waitlist: ["b"]);
		var alloc = Frozen(acts, [x, y], ("x", "b", false), ("y", "c", false));

		var moves = alloc.FillFreeSeats(1, 1);

		Assert.Equal(["a"], Seats(alloc, "x"));
		Assert.Equal(["b"], Seats(alloc, "y"));
		Assert.Equal(2, moves.Count);
		Assert.Equal("b1", moves[0].GaveUp?.Id);
		Assert.Equal("c1", moves[1].GaveUp?.Id);
		Assert.Equal(0, alloc.Load(acts[2].Sessions[0]));
	}

	[Fact]
	public void Without_a_tick_nobody_gives_up_a_seat()
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 1, hour: 12)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 40 });
		var alloc = Frozen(acts, [x], ("x", "b", false));

		Assert.Empty(alloc.FillFreeSeats(1, 1));
		Assert.Equal(["b"], Seats(alloc, "x"));
	}

	[Fact]
	public void Moving_up_stays_within_the_group()
	{
		// x ticked green g2 but holds only a blue seat with fewer points: blue must not be swapped for green.
		ActivityGroup[] groups = [new("green", "Grün", 1, 1), new("blue", "Blau", 1, 1, "blue")];
		Activity[] acts = [Act("g1", 1, "green", 9), Act("g2", 1, "green", 12), Act("g3", 1, "green", 15), Act("b1", 1, "blue", 18)];
		var x = Ballot("x", new() { ["g1"] = 50, ["g2"] = 10, ["b1"] = 5 }, waitlist: ["g2"]);
		var y = Ballot("y", new() { ["g2"] = 80, ["g3"] = 20 }, waitlist: ["g2"]);
		var alloc = FrozenWithGroups(acts, [x, y], groups, ("x", "g1"), ("x", "b1"), ("y", "g3"));

		Assert.False(alloc.MoveUpFor(x, acts[1], 2).Possible);
		var (possible, givesUp) = alloc.MoveUpFor(y, acts[1], 2);
		Assert.True(possible);
		Assert.Equal("g31", givesUp?.Id);

		alloc.FillFreeSeats(2, 1);

		Assert.Equal(["b1", "g1"], Seats(alloc, "x"));
		Assert.Equal(["g2"], Seats(alloc, "y"));
	}

	[Fact]
	public void A_given_back_activity_is_never_allocated_again()
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 1, hour: 12)];
		var x = Ballot("x", new() { ["a"] = 90, ["b"] = 10 }, waitlist: ["a"], declined: ["a"]);
		var alloc = Frozen(acts, [x]);

		alloc.FillFreeSeats(1, 1);

		Assert.Equal(["b"], Seats(alloc, "x"));
		Assert.False(alloc.MoveUpFor(x, acts[0], 1).Possible);
		Assert.DoesNotContain(alloc.OpenSeats(x, 1), s => s.ActivityId == "a");
	}

	[Fact]
	public void A_locked_seat_is_never_given_up()
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 1, hour: 12)];
		var x = Ballot("x", new() { ["a"] = 90, ["b"] = 10 }, waitlist: ["a"]);
		var alloc = Frozen(acts, [x], ("x", "b", true));

		Assert.False(alloc.MoveUpFor(x, acts[0], 1).Possible);
		Assert.Empty(alloc.FillFreeSeats(1, 1));
		Assert.Equal(["b"], Seats(alloc, "x"));
	}

	[Fact]
	public void Without_replacement_a_missing_seat_stays_open_unless_ticked()
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 1, hour: 12)];
		var x = Ballot("x", new() { ["a"] = 50, ["b"] = 50 }, waitlist: ["b"], noReplacement: [""]);
		var alloc = Frozen(acts, [x]);

		var move = Assert.Single(alloc.FillFreeSeats(1, 1));

		Assert.Equal("b1", move.Session.Id);
		Assert.False(move.Need);
	}

	[Fact]
	public void Missing_and_open_seats_follow_the_group_quota()
	{
		ActivityGroup[] groups = [new("green", "Grün", 2, 2), new("blue", "Blau", 1, 1, "blue")];
		Activity[] acts = [Act("g1", 1, "green", 9), Act("g2", 1, "green", 12), Act("g3", 0, "green", 15), Act("b1", 1, "blue", 18)];
		var x = Ballot("x", new() { ["g1"] = 50, ["b1"] = 50 });
		var alloc = FrozenWithGroups(acts, [x], groups, ("x", "g1"), ("x", "b1"));

		var missing = Assert.Single(alloc.MissingSeats(x, 3));
		Assert.Equal("green", missing.Group?.Id);
		Assert.Equal(1, missing.Count);
		// g2 has no points but is free; g3 is full; b1 is already held and blue is complete.
		Assert.Equal(["g21"], alloc.OpenSeats(x, 3).Select(s => s.Id));
	}

	[Fact]
	public void Without_groups_missing_seats_compare_with_the_seats_per_person()
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 1, hour: 12), Act("c", 1, hour: 9)];
		var x = Ballot("x", new() { ["a"] = 50 });
		var alloc = Frozen(acts, [x], ("x", "a", false));

		var missing = Assert.Single(alloc.MissingSeats(x, 2));
		Assert.Null(missing.Group);
		Assert.Equal(1, missing.Count);
		// c clashes with a.
		Assert.Equal(["b1"], alloc.OpenSeats(x, 2).Select(s => s.Id));
	}

	[Fact]
	public void The_next_for_an_offered_seat_follows_moving_up_and_skips_who_was_asked()
	{
		// a is full; x ticked it with more points than y, z gave it no points.
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 5, hour: 12)];
		var x = Ballot("x", new() { ["a"] = 70, ["b"] = 30 }, waitlist: ["a"]);
		var y = Ballot("y", new() { ["a"] = 60, ["b"] = 40 }, waitlist: ["a"]);
		var z = Ballot("z", new() { ["b"] = 100 });
		var alloc = Frozen(acts, [x, y, z], ("x", "b", false), ("y", "b", false), ("z", "a", false));

		var first = alloc.NextFor(acts[0].Sessions[0], 1, 1, _ => false);
		var second = alloc.NextFor(acts[0].Sessions[0], 1, 1, b => b.Id == "x");

		Assert.Equal("x", first?.BallotId);
		Assert.Equal("b1", first?.GaveUp?.Id);
		Assert.Equal("y", second?.BallotId);
		Assert.Null(alloc.NextFor(acts[0].Sessions[0], 1, 1, b => b.Id != "z"));
		Assert.Equal("b1", alloc.ExchangeFor(x, acts[0].Sessions[0], 1).GivesUp?.Id);
	}

	[Fact]
	public void Organisers_may_add_any_session_without_a_clash_in_a_group_with_a_missing_seat()
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 0, hour: 12), Act("c", 1, hour: 9)];
		var x = Ballot("x", new() { ["a"] = 50 }, declined: ["b"]);
		var alloc = Frozen(acts, [x], ("x", "a", false));

		Assert.True(alloc.CanAdd(x, acts[1].Sessions[0], 2)); // full and given back: organisers decide
		Assert.False(alloc.CanAdd(x, acts[2].Sessions[0], 2)); // clashes with a
		Assert.False(alloc.CanAdd(x, acts[0].Sessions[0], 2)); // already held
		Assert.False(alloc.CanAdd(x, acts[1].Sessions[0], 1)); // no seat missing
	}

	// x holds b (10) and c (30), ticked a (60): b is the weakest and pre-selected, c can be chosen instead.
	private static (Activity[] Acts, Allocator Alloc, Ballot X) TwoSeats(Dictionary<string, string>? giveUp, bool lockC = false)
	{
		Activity[] acts = [Act("a", 1, hour: 9), Act("b", 1, hour: 12), Act("c", 1, hour: 15)];
		var x = Ballot("x", new() { ["a"] = 60, ["b"] = 10, ["c"] = 30 }, waitlist: ["a"]) with { GiveUp = giveUp };
		return (acts, Frozen(acts, [x], ("x", "b", false), ("x", "c", lockC)), x);
	}

	[Fact]
	public void Give_up_options_list_every_seat_that_could_go_weakest_first()
	{
		var (acts, alloc, x) = TwoSeats(null);

		Assert.Equal(["b", "c"], alloc.GiveUpOptions(x, acts[0], 2).Select(s => s.ActivityId));
		Assert.Empty(alloc.GiveUpOptions(x, acts[0], 3)); // a seat missing: nothing to give up
	}

	[Fact]
	public void Without_a_choice_the_weakest_seat_goes()
	{
		var (_, alloc, _) = TwoSeats(null);

		var move = Assert.Single(alloc.FillFreeSeats(2, 1));

		Assert.Equal("b1", move.GaveUp?.Id);
		Assert.Equal(["a", "c"], Seats(alloc, "x"));
	}

	[Fact]
	public void The_chosen_seat_goes_instead_of_the_weakest()
	{
		var (acts, alloc, x) = TwoSeats(new() { ["a"] = "c" });

		Assert.Equal("c", alloc.MoveUpFor(x, acts[0], 2).GivesUp?.ActivityId);
		var move = Assert.Single(alloc.FillFreeSeats(2, 1));

		Assert.Equal("c1", move.GaveUp?.Id);
		Assert.Equal(["a", "b"], Seats(alloc, "x"));
	}

	[Fact]
	public void A_chosen_seat_that_cannot_go_blocks_moving_up_instead_of_giving_up_another()
	{
		var (acts, alloc, x) = TwoSeats(new() { ["a"] = "c" }, lockC: true);

		Assert.False(alloc.MoveUpFor(x, acts[0], 2).Possible);
		Assert.Empty(alloc.FillFreeSeats(2, 1));
		Assert.Equal(["b", "c"], Seats(alloc, "x"));
		Assert.Equal(["b"], alloc.GiveUpOptions(x, acts[0], 2).Select(s => s.ActivityId));
	}

	[Fact]
	public void A_choice_for_a_seat_no_longer_held_falls_back_to_the_weakest()
	{
		var (_, alloc, _) = TwoSeats(new() { ["a"] = "d" });

		Assert.Equal("b1", Assert.Single(alloc.FillFreeSeats(2, 1)).GaveUp?.Id);
	}
}
