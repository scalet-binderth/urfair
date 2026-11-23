using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;

namespace Urfair.Api;

/// <summary>
/// Automatic deletion (data of minors): a vote is deleted with all its data (participants, ballots, seats, mails, log,
/// save points, offers) <see cref="Days"/> days after it became Ended (Retention:Days, default 90). Organisers can delete earlier.
/// </summary>
public static class Retention
{
	public const int DefaultDays = 90;

	public static int Days(IConfiguration cfg) => int.TryParse(cfg["Retention:Days"], out var d) && d > 0 ? d : DefaultDays;

	/// <summary>The day the vote is deleted, or null while it has not ended.</summary>
	public static DateOnly? DeleteOn(DateTime? endedAt, int days) => endedAt is { } e ? DateOnly.FromDateTime(e.AddDays(days)) : null;

	/// <summary>Deletes ended votes whose period is over; returns their names for the server log.</summary>
	public static async Task<List<string>> DeleteExpiredAsync(AppDbContext db, int days)
	{
		var limit = Phases.Now().AddDays(-days);
		var expired = await db.FairSlots.Where(s => s.Phase == Phase.Ended && s.EndedAt != null && s.EndedAt <= limit).ToListAsync();
		if (expired.Count == 0) return [];
		db.FairSlots.RemoveRange(expired); // everything else goes with it (cascade)
		await db.SaveChangesAsync();
		return expired.Select(s => s.Name).ToList();
	}
}
