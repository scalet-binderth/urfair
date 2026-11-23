using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;

namespace Urfair.Api;

/// <summary>
/// Once a leader got a list by e-mail („Teilnahmeliste“), changes to it are mailed to them: who is new, who is no longer there,
/// and the whole list. Changes by participants (cancelling, giving back, moving up, accepting an offer) are mailed at once;
/// other changes (e.g. by hand) once the vote has been quiet for a while, so a series of edits makes one mail. Only while the
/// allocation is visible or closed; changes made meanwhile are mailed when it is again.
/// </summary>
public static class LeaderNotices
{
	static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

	/// <summary>The names on a list as stored in <see cref="SessionEntity.LeaderList"/>.</summary>
	public static string ListText(IEnumerable<string> names) => string.Join('\n', names.Order(StringComparer.CurrentCultureIgnoreCase));

	/// <summary>Mails each leader the changes to their lists since the last mail; returns the number of mails.</summary>
	public static async Task<int> SendAsync(AppDbContext db, FairSlotEntity slot)
	{
		if (slot.Phase is not (Phase.Published or Phase.Closed) || slot.FrozenAt is null) return 0;
		var activities = await db.Activities.Include(a => a.Sessions)
			.Where(a => a.FairSlotId == slot.Id && a.LeaderEmail != null && a.Sessions.Any(s => s.LeaderList != null)).ToListAsync();
		if (activities.Count == 0) return 0;
		var names = await db.Ballots.Where(b => b.FairSlotId == slot.Id).ToDictionaryAsync(b => b.Id, b => b.Name);
		var seated = (await db.Assignments.Where(a => a.FairSlotId == slot.Id).Select(a => new { a.SessionId, a.BallotId }).ToListAsync())
			.ToLookup(a => a.SessionId, a => names[a.BallotId]);
		var count = 0;
		foreach (var leader in activities.GroupBy(a => a.LeaderEmail!.ToLowerInvariant()))
		{
			var changes = new StringBuilder();
			foreach (var a in leader.OrderBy(a => a.SortOrder))
			foreach (var s in a.Sessions.Where(s => s.LeaderList != null).OrderBy(s => s.Start))
			{
				var now = seated[s.Id].Order(StringComparer.CurrentCultureIgnoreCase).ToList();
				var before = s.LeaderList!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
				var added = now.Except(before, StringComparer.OrdinalIgnoreCase).ToList();
				var gone = before.Except(now, StringComparer.OrdinalIgnoreCase).ToList();
				if (added.Count == 0 && gone.Count == 0) continue;
				s.LeaderList = ListText(now);
				changes.Append($"\n{a.Title}\n{AllocationEndpoints.When(s.Start, s.End, s.Flexible)}");
				if (s.Location.Length > 0) changes.Append($" · {s.Location}");
				changes.Append('\n');
				if (added.Count > 0) changes.Append($"Neu dabei: {string.Join(", ", added)}\n");
				if (gone.Count > 0) changes.Append($"Nicht mehr dabei: {string.Join(", ", gone)}\n");
				changes.Append(s.Capacity is { } c ? $"Jetzt {now.Count} von {c} Plätzen:\n" : $"Jetzt {now.Count} Personen:\n");
				if (now.Count == 0) changes.Append("Niemand zugeteilt.\n");
				foreach (var (p, i) in now.Select((p, i) => (p, i))) changes.Append($"{i + 1,3}. {p}\n");
			}
			if (changes.Length == 0) continue;
			var name = leader.Select(a => a.Leader).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
			var body = new StringBuilder(name is null ? "Hallo,\n\n" : $"Hallo {name},\n\n")
				.Append($"bei „{slot.Name}“ hat sich deine Teilnahmeliste geändert.\n")
				.Append(changes)
				.Append("\nÄndert sich noch etwas, bekommst du wieder eine Mail.\n\n").Append(slot.Organiser ?? "Dein Organisationsteam");
			Mailing.Add(db, slot, new MailEntity { To = leader.First().LeaderEmail!, Subject = $"Änderung Teilnahmeliste: {slot.Name}", Body = body.ToString() });
			count++;
		}
		await db.SaveChangesAsync();
		return count;
	}

	/// <summary>Once a minute: changes not mailed yet (e.g. by hand), for votes without a change in the last minutes.</summary>
	public static async Task TickAsync(AppDbContext db)
	{
		var listed = await db.Activities.Where(a => a.LeaderEmail != null && a.Sessions.Any(s => s.LeaderList != null))
			.Select(a => a.FairSlotId).Distinct().ToListAsync();
		var quietSince = DateTime.UtcNow - Quiet;
		foreach (var slot in await db.FairSlots.Where(s => listed.Contains(s.Id) && (s.Phase == Phase.Published || s.Phase == Phase.Closed)).ToListAsync())
		{
			if (await db.AllocationLog.AnyAsync(l => l.FairSlotId == slot.Id && l.At > quietSince)) continue;
			await SendAsync(db, slot);
		}
	}
}
