using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;

namespace Urfair.Api;

/// <summary>
/// A new vote from an existing one (e.g. last year's) as a template: settings, rules, groups, activities and dates,
/// the dates shifted by whole weeks (52 = the same weekday a year later). Participants, points, seats, mails and the log
/// stay behind; the copy starts as a draft.
/// </summary>
public static class SlotCopy
{
	public const int MaxShiftWeeks = 104;

	public static async Task<FairSlotEntity> CopyAsync(AppDbContext db, FairSlotEntity source, string name, int shiftWeeks)
	{
		var shift = TimeSpan.FromDays(7 * shiftWeeks);
		var copy = new FairSlotEntity
		{
			Name = name,
			RegistrationDeadline = source.RegistrationDeadline + shift,
			Seed = source.Seed,
			OpenToAll = source.OpenToAll,
			ShowInterim = source.ShowInterim,
			Parish = source.Parish,
			Organiser = source.Organiser,
			OrganiserEmail = source.OrganiserEmail,
			Visibility = source.Visibility,
		};
		copy.Apply(source.ToRules());
		var activities = await db.Activities.Include(a => a.Sessions).AsNoTracking()
			.Where(a => a.FairSlotId == source.Id).OrderBy(a => a.SortOrder).ToListAsync();
		foreach (var a in activities)
			copy.Activities.Add(new ActivityEntity
			{
				Title = a.Title,
				RestrictedTo = a.RestrictedTo,
				Popularity = a.Popularity,
				GroupId = a.GroupId,
				Description = a.Description,
				Parish = a.Parish,
				Leader = a.Leader,
				LeaderEmail = a.LeaderEmail,
				SortOrder = a.SortOrder,
				Sessions = a.Sessions.Select(s => new SessionEntity
				{
					Start = s.Start + shift,
					End = s.End + shift,
					Location = s.Location,
					Capacity = s.Capacity,
					Flexible = s.Flexible,
				}).ToList(),
			});
		return copy;
	}
}
