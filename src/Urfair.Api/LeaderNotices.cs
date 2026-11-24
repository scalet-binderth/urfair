using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;

namespace Urfair.Api;

/// <param name="Added">New on the list since the leader's last mail.</param>
/// <param name="Removed">On the last mail, no longer on the list.</param>
/// <param name="Count">People on the list now.</param>
public record LeaderSessionChangeDto(string ActivityId, string Title, string SessionId, DateTime Start, DateTime? End, bool Flexible, string Location,
	int? Capacity, IReadOnlyList<string> Added, IReadOnlyList<string> Removed, int Count);
public record LeaderChangeDto(string Email, string? Name, IReadOnlyList<LeaderSessionChangeDto> Sessions);
public record LeaderChangesRequest(List<string>? Emails);

/// <summary>
/// Once a leader got a list by e-mail („Teilnahmeliste“), the list they got is kept per date. Changes against it (by hand,
/// cancelling, giving back, moving up, accepting an offer) are only collected: the organisers see who does not know about
/// a change yet and decide whom to mail („Änderung Teilnahmeliste“: who is new, who is no longer there, the whole list).
/// </summary>
public static class LeaderNotices
{
	/// <summary>The names on a list as stored in <see cref="SessionEntity.LeaderList"/>.</summary>
	public static string ListText(IEnumerable<string> names) => string.Join('\n', names.Order(StringComparer.CurrentCultureIgnoreCase));

	sealed record SessionChange(ActivityEntity Activity, SessionEntity Session, List<string> Now, List<string> Added, List<string> Removed);
	sealed record Pending(string Email, string? Name, List<SessionChange> Sessions);

	/// <summary>Per leader e-mail address the dates whose list changed since the last mail to them.</summary>
	static async Task<List<Pending>> PendingAsync(AppDbContext db, FairSlotEntity slot)
	{
		if (slot.FrozenAt is null) return [];
		var activities = await db.Activities.Include(a => a.Sessions)
			.Where(a => a.FairSlotId == slot.Id && a.Sessions.Any(s => s.LeaderList != null)).ToListAsync();
		if (activities.Count == 0) return [];
		var names = await db.Ballots.Where(b => b.FairSlotId == slot.Id).ToDictionaryAsync(b => b.Id, b => b.Name);
		var seated = (await db.Assignments.Where(a => a.FairSlotId == slot.Id).Select(a => new { a.SessionId, a.BallotId }).ToListAsync())
			.ToLookup(a => a.SessionId, a => names[a.BallotId]);
		var pending = new List<Pending>();
		foreach (var leader in activities.Where(a => Leaders.EmailOf(a, slot) is not null).GroupBy(a => Leaders.EmailOf(a, slot)!.ToLowerInvariant()))
		{
			var sessions = new List<SessionChange>();
			foreach (var a in leader.OrderBy(a => a.SortOrder))
			foreach (var s in a.Sessions.Where(s => s.LeaderList != null).OrderBy(s => s.Start))
			{
				var now = seated[s.Id].Order(StringComparer.CurrentCultureIgnoreCase).ToList();
				var before = s.LeaderList!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
				var added = now.Except(before, StringComparer.OrdinalIgnoreCase).ToList();
				var removed = before.Except(now, StringComparer.OrdinalIgnoreCase).ToList();
				if (added.Count > 0 || removed.Count > 0) sessions.Add(new SessionChange(a, s, now, added, removed));
			}
			if (sessions.Count > 0) pending.Add(new Pending(Leaders.EmailOf(leader.First(), slot)!, Leaders.NameOf(leader.First(), slot), sessions));
		}
		return pending;
	}

	/// <summary>Leaders who do not know about changes to their lists yet.</summary>
	public static async Task<List<LeaderChangeDto>> ChangesAsync(AppDbContext db, FairSlotEntity slot) =>
		(await PendingAsync(db, slot)).Select(p => new LeaderChangeDto(p.Email, p.Name, p.Sessions.Select(c =>
			new LeaderSessionChangeDto(c.Activity.Id, c.Activity.Title, c.Session.Id, c.Session.Start, c.Session.End, c.Session.Flexible, c.Session.Location,
				c.Session.Capacity, c.Added, c.Removed, c.Now.Count)).ToList())).ToList();

	/// <summary>Mails the chosen leaders (all if <paramref name="emails"/> is null) the changes since their last mail; returns the number of mails.</summary>
	public static async Task<int> SendAsync(AppDbContext db, FairSlotEntity slot, IReadOnlyCollection<string>? emails = null)
	{
		var wanted = emails?.Select(e => e.Trim().ToLowerInvariant()).ToHashSet();
		var count = 0;
		foreach (var leader in (await PendingAsync(db, slot)).Where(p => wanted is null || wanted.Contains(p.Email.ToLowerInvariant())))
		{
			var changes = new StringBuilder();
			foreach (var c in leader.Sessions)
			{
				var s = c.Session;
				s.LeaderList = ListText(c.Now);
				changes.Append($"\n{c.Activity.Title}\n{AllocationEndpoints.When(s.Start, s.End, s.Flexible)}");
				if (s.Location.Length > 0) changes.Append($" · {s.Location}");
				changes.Append('\n');
				if (c.Added.Count > 0) changes.Append($"Neu dabei: {string.Join(", ", c.Added)}\n");
				if (c.Removed.Count > 0) changes.Append($"Nicht mehr dabei: {string.Join(", ", c.Removed)}\n");
				changes.Append(s.Capacity is { } cap ? $"Jetzt {c.Now.Count} von {cap} Plätzen:\n" : $"Jetzt {c.Now.Count} Personen:\n");
				if (c.Now.Count == 0) changes.Append("Niemand zugeteilt.\n");
				foreach (var (p, i) in c.Now.Select((p, i) => (p, i))) changes.Append($"{i + 1,3}. {p}\n");
			}
			var body = new StringBuilder(leader.Name is null ? "Hallo,\n\n" : $"Hallo {leader.Name},\n\n")
				.Append($"bei „{slot.Name}“ hat sich deine Teilnahmeliste geändert.\n")
				.Append(changes)
				.Append("\nÄndert sich noch etwas, melden wir uns wieder.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
			Mailing.Add(db, slot, new MailEntity { To = leader.Email, Subject = $"Änderung Teilnahmeliste: {slot.Name}", Body = body.ToString() });
			count++;
		}
		await db.SaveChangesAsync();
		return count;
	}
}
