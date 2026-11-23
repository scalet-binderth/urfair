using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Urfair.Core;

namespace Urfair.Api.Data;

public static class Database
{
    /// <summary>
    /// Demo mode (default on): demo people, sample data and reset buttons. A real instance sets
    /// Demo__Enabled=false; the same code then runs without any demo content.
    /// </summary>
    public static bool IsDemo(IConfiguration config) => config.GetValue("Demo:Enabled", true);

    /// <summary>
    /// Chooses the database: PostgreSQL if DATABASE_URL (as provided by Neon, Render, Koyeb…) or
    /// Database:Provider=Postgres is set, otherwise a local SQLite file.
    /// </summary>
    public static void AddAppDatabase(this WebApplicationBuilder builder)
    {
        // Store DateTime as "timestamp without time zone"; all dates are local (Europe/Vienna) event times.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var config = builder.Configuration;
        var url = config["DATABASE_URL"];
        var provider = config["Database:Provider"] ?? (string.IsNullOrEmpty(url) ? "Sqlite" : "Postgres");
        var connection = config.GetConnectionString("Default");

        // The app works with AppDbContext; the provider-specific subclass brings the matching migrations.
        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            builder.Services.AddDbContext<AppDbContext, PostgresAppDbContext>(o => o.UseNpgsql(string.IsNullOrEmpty(url) ? connection : FromUrl(url)));
        else
            builder.Services.AddDbContext<AppDbContext, SqliteAppDbContext>(o => o.UseSqlite(connection ?? "Data Source=urfair.db"));
    }

    /// <summary>Converts postgres://user:pass@host:port/db?sslmode=require into an Npgsql connection string.</summary>
    public static string FromUrl(string url)
    {
        var uri = new Uri(url);
        var user = uri.UserInfo.Split(':', 2);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
            SslMode = SslMode.Require,
        };
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (Enum.TryParse<SslMode>(query["sslmode"], true, out var ssl)) csb.SslMode = ssl;
        return csb.ConnectionString;
    }

    /// <summary>Applies pending migrations and, in demo mode, seeds the demo FairSlots if there are none.</summary>
    public static async Task InitialiseAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var creator = db.GetService<IRelationalDatabaseCreator>();

        // One-time switch: databases created before migrations existed only hold demo data and are recreated.
        if (await creator.ExistsAsync() && await creator.HasTablesAsync() && !(await db.Database.GetAppliedMigrationsAsync()).Any())
        {
            logger.LogWarning("Database without migration history: recreating the (demo) database.");
            await WipeAsync(db);
        }

        await db.Database.MigrateAsync();

        // Participants from before personal links get their token now.
        var withoutToken = await db.Participants.Where(p => p.Token == "").ToListAsync();
        foreach (var p in withoutToken) p.Token = ParticipantEntity.NewToken();
        if (withoutToken.Count > 0) await db.SaveChangesAsync();

        // Allocations frozen before the original was kept: the stored seats become the original.
        var frozenIds = await db.FairSlots.Where(s => s.FrozenAt != null).Select(s => s.Id).ToListAsync();
        var withOriginal = await db.OriginalAssignments.Select(a => a.FairSlotId).Distinct().ToListAsync();
        var missing = frozenIds.Except(withOriginal).ToList();
        if (missing.Count > 0)
        {
            db.OriginalAssignments.AddRange((await db.Assignments.AsNoTracking().Where(a => missing.Contains(a.FairSlotId)).ToListAsync())
                .Select(a => new OriginalAssignmentEntity { FairSlotId = a.FairSlotId, BallotId = a.BallotId, SessionId = a.SessionId }));
            await db.SaveChangesAsync();
        }

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (IsDemo(config))
        {
            await AuthEndpoints.SeedDemoAccountsAsync(db);
            if (!await db.FairSlots.AnyAsync()) await SeedDemoAsync(db);
        }
    }

    /// <summary>Removes all FairSlots and seeds the demo again (demo mode only).</summary>
    public static async Task ResetDemoAsync(AppDbContext db)
    {
        db.FairSlots.RemoveRange(await db.FairSlots.ToListAsync());
        await db.SaveChangesAsync();
        await SeedDemoAsync(db);
    }

    /// <summary>
    /// Five demo FairSlots for presentations, one per phase. Demo people: A sees nothing, B and C two in registration and one
    /// with a visible allocation, D additionally one closed.
    /// B, C and D have already voted in their open FairSlots and can change their ballots.
    /// Dates are relative to the day of seeding, so deadlines and sessions fit the phases whenever the demo is reset.
    /// "Pfarrfest" shows the interim status during registration and, as it needs every shift filled (required staffing), how
    /// many people are still missing on each date; D offered one shift more than required. In the visible "Ministrantenausflug" sample people are on
    /// waiting lists, so a seat given back moves up. There C did not get her first wish (Kletterwald, full) and can tick it,
    /// while D holds a seat there he can give back: then C moves up first, as no ticked sample person gave it as many points.
    /// All its activities are full: B sits in his weakest wish and is warned that giving it back leaves him without a seat.
    /// In the closed "Ferienprogramm" D can cancel a seat; the cancellation mail to the organiser is in the demo mailbox. When the
    /// organisers fill his seat in "Kochen mit Oma", C is offered it (she ticked it) and accepts or declines under her link.
    /// </summary>
    private static async Task SeedDemoAsync(AppDbContext db)
    {
        var (b, c, d) = (DemoData.B.Name, DemoData.C.Name, DemoData.D.Name);
        var today = DateOnly.FromDateTime(Phases.Now());
        var firmung = await CreateSlotAsync(db, "Firmvorbereitung Urfahr (Demo)", Phase.Open, DeadlineIn(14),
            SampleData.FirmungRules with { Friends = 3 }, SampleData.Activities().MovedTo(today.AddDays(17)), 140, [b, c, d], withBallots: [b, c, d], parish: SampleData.FirmungParish, organiser: SampleData.FirmungOrganiser, visibility: Visibility.JuniaReadOnly);
        // B and C entered the same friend code: they wish to share an activity.
        foreach (var x in await db.Ballots.Where(x => x.FairSlotId == firmung.Id && (x.Name == b || x.Name == c)).ToListAsync()) x.Friends = DemoData.FriendCode;
        await CreateSlotAsync(db, "Ministrantenausflug", Phase.Published, DeadlineIn(-7),
            DemoData.ExcursionRules, DemoData.Excursion().MovedTo(today.AddDays(14)), 40, [b, c, d], withBallots: [b, c, d], parish: "Pfarrgemeinde Urfahr-Hl. Geist", organiser: "Miriam Koller", visibility: Visibility.ParishOnly,
            choices: DemoData.ExcursionChoices, seats: DemoData.ExcursionSeats, organiserEmail: "miriam.koller@example.org");
        await CreateSlotAsync(db, "Ferienprogramm", Phase.Closed, DeadlineIn(-21),
            DemoData.HolidayRules, DemoData.HolidayProgramme().MovedTo(today.AddDays(7)), 60, [c, d], withBallots: [c, d], parish: "Pfarrgemeinde Urfahr-St. Magdalena", organiser: "Stefan Huber",
            organiserEmail: "stefan.huber@example.org", choices: DemoData.HolidayChoices, seats: DemoData.HolidaySeats, ticks: DemoData.HolidayTicks);
        await CreateSlotAsync(db, "Jungschar-Sommerlager", Phase.Draft, DeadlineIn(180),
            DemoData.CampRules, DemoData.Camp().MovedTo(today.AddDays(220)), 0, [], parish: "Pfarrgemeinde Urfahr-Christkönig", organiser: "Jungschar Christkönig", visibility: Visibility.ParishOnly);
        var festival = await CreateSlotAsync(db, "Pfarrfest – Mithelfen", Phase.Open, DeadlineIn(30),
            DemoData.FestivalRules, DemoData.ParishFestival().MovedTo(today.AddDays(45)), 0, [], withBallots: [b, c, d], openToAll: true, parish: "Pfarrgemeinde Urfahr-St. Markus", organiser: "Maria Pichler", visibility: Visibility.ParishOnly, showInterim: true);
        // D would also take one shift more than required.
        (await db.Ballots.SingleAsync(x => x.FairSlotId == festival.Id && x.Name == d)).Extra = 1;
        await db.SaveChangesAsync();
    }

    /// <summary>A registration deadline at the end of the day, relative to today.</summary>
    private static DateTime DeadlineIn(int days) => Phases.Now().Date.AddDays(days).AddMinutes(23 * 60 + 59);

    /// <summary>Creates a FairSlot from the Urfahr sample activities (used for "create with sample data").</summary>
    public static Task<FairSlotEntity> CreateDemoSlotAsync(AppDbContext db, string name, Phase phase, int sampleCount) =>
        CreateSlotAsync(db, name, phase, DeadlineIn(14), SampleData.FirmungRules, SampleData.Activities().MovedTo(DateOnly.FromDateTime(Phases.Now()).AddDays(17)),
            sampleCount, [], parish: SampleData.FirmungParish, organiser: SampleData.FirmungOrganiser);

    /// <summary>Creates a FairSlot with activities, sample ballots, a participant list and optionally real ballots for some participants.</summary>
    /// <param name="choices">Fixed points (activity title → points) for some of <paramref name="withBallots"/>; the others get generated ones.</param>
    /// <param name="seats">After freezing: the activities (titles) a person sits in, swapped with sample people (see <see cref="SeatAsync"/>).</param>
    /// <param name="ticks">Waiting-list ticks (person, activity title), as if set while the allocation was visible.</param>
    private static async Task<FairSlotEntity> CreateSlotAsync(AppDbContext db, string name, Phase phase, DateTime deadline, Rules rules,
        IReadOnlyList<Activity> activities, int sampleCount, string[] participants, string[]? withBallots = null, bool openToAll = false,
        string? parish = null, string? organiser = null, Visibility visibility = Visibility.JuniaShared, bool showInterim = false,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? choices = null, IReadOnlyList<(string Name, string Title)>? seats = null,
        string? organiserEmail = null, IReadOnlyList<(string Name, string Title)>? ticks = null)
    {
        var slot = new FairSlotEntity { Name = name, Phase = phase, SampleCount = sampleCount, RegistrationDeadline = deadline, OpenToAll = openToAll, Parish = parish, Organiser = organiser, OrganiserEmail = organiserEmail, Visibility = visibility, ShowInterim = showInterim };
        slot.Apply(rules);
        var order = 0;
        foreach (var a in activities) slot.Activities.Add(ToEntity(a, slot.Id, order++));
        slot.Participants = participants.Select(p => DemoData.People.FirstOrDefault(x => x.Name == p) is { } d
            ? new ParticipantEntity { FairSlotId = slot.Id, Name = p, Email = d.Email, BirthDate = d.BirthDate, BaptismDate = d.BaptismDate, Parish = d.Parish }
            : new ParticipantEntity { FairSlotId = slot.Id, Name = p }).ToList();
        db.FairSlots.Add(slot);
        await db.SaveChangesAsync();
        await ResetSamplesAsync(db, slot.Id);

        if (withBallots is { Length: > 0 })
        {
            // Demo people have no gender on record, so leave out activities restricted to boys or girls.
            var models = (await LoadActivitiesAsync(db, slot.Id)).Where(a => a.RestrictedTo is null).ToList();
            var generated = SampleData.Participants(withBallots.Length, slot.ToRules(), models, slot.Seed + 1);
            for (var i = 0; i < withBallots.Length; i++)
            {
                var points = choices?.GetValueOrDefault(withBallots[i]) is { } chosen
                    ? chosen.ToDictionary(x => models.Single(a => a.Title == x.Key).Id, x => x.Value)
                    : generated[i].Points;
                db.Ballots.Add(new BallotEntity
                {
                    FairSlotId = slot.Id, Name = withBallots[i],
                    Points = points.Select(p => new BallotPoints { ActivityId = p.Key, Points = p.Value }).ToList(),
                    UnavailableSessions = choices?.ContainsKey(withBallots[i]) == true ? [] : generated[i].UnavailableSessions.ToList(),
                });
            }
            await db.SaveChangesAsync();
        }
        if (Phases.HasAllocation(phase)) await AllocationEndpoints.FreezeAsync(db, slot);
        foreach (var person in (seats ?? []).GroupBy(s => s.Name)) await SeatAsync(db, slot, person.Key, person.Select(s => s.Title).ToList());
        if (phase == Phase.Published) await TickWaitlistsAsync(db, slot);
        if (ticks is { Count: > 0 })
        {
            var ids = slot.Activities.ToDictionary(a => a.Title, a => a.Id);
            foreach (var b in await db.Ballots.Where(b => b.FairSlotId == slot.Id && !b.IsSample).ToListAsync())
                b.Waitlist = ticks.Where(t => t.Name == b.Name).Select(t => ids[t.Title]).ToList();
            await db.SaveChangesAsync();
        }
        if (Phases.HasAllocation(phase))
        {
            // The first save point: the demo can be undone back to here.
            AllocationEndpoints.Log(db, slot.Id, Phases.Clock, "frozen");
            await db.SaveChangesAsync();
        }
        return slot;
    }

    /// <summary>
    /// Demo: puts a person into activities of the frozen allocation (one date each). For each one they lack, a seat of theirs
    /// they should not keep is swapped with a sample person sitting there (preferring one without the old activity, with time
    /// for it and who gave it points); without such a seat the sample person's seat is taken. The original allocation is set
    /// to the result, so the demo starts without differences from the original.
    /// </summary>
    private static async Task SeatAsync(AppDbContext db, FairSlotEntity slot, string person, IReadOnlyList<string> titles)
    {
        var activities = await db.Activities.Include(a => a.Sessions).Where(a => a.FairSlotId == slot.Id).ToListAsync();
        var activityOf = activities.SelectMany(a => a.Sessions).ToDictionary(s => s.Id, s => s.ActivityId);
        var wanted = titles.Select(t => activities.Single(a => a.Title == t).Sessions.Single().Id).ToList();
        var ballots = await db.Ballots.Include(b => b.Points).Where(b => b.FairSlotId == slot.Id).ToListAsync();
        var me = ballots.Single(b => b.Name == person);
        var seats = await db.Assignments.Where(a => a.FairSlotId == slot.Id).ToListAsync();
        bool Holds(BallotEntity b, string activityId) => seats.Any(a => a.BallotId == b.Id && activityOf[a.SessionId] == activityId);
        foreach (var target in wanted)
        {
            var mine = seats.Where(a => a.BallotId == me.Id).ToList();
            if (mine.Any(a => a.SessionId == target)) continue;
            var spare = mine.FirstOrDefault(a => !wanted.Contains(a.SessionId));
            var old = spare?.SessionId;
            var sample = ballots.Where(b => b.IsSample && seats.Any(a => a.BallotId == b.Id && a.SessionId == target))
                .OrderBy(b => old is not null && Holds(b, activityOf[old]))
                .ThenBy(b => old is not null && b.UnavailableSessions.Contains(old))
                .ThenByDescending(b => old is null ? 0 : b.Points.FirstOrDefault(p => p.ActivityId == activityOf[old])?.Points ?? 0).FirstOrDefault();
            var theirs = sample is null ? null : seats.First(a => a.BallotId == sample.Id && a.SessionId == target);
            if (spare is not null) spare.SessionId = target;
            if (theirs is not null && old is not null) theirs.SessionId = old;
            else if (theirs is not null) theirs.BallotId = me.Id;
            if (spare is null && theirs is null)
            {
                var seat = new AssignmentEntity { FairSlotId = slot.Id, BallotId = me.Id, SessionId = target };
                seats.Add(seat);
                db.Assignments.Add(seat);
            }
        }
        db.OriginalAssignments.RemoveRange(await db.OriginalAssignments.Where(a => a.FairSlotId == slot.Id).ToListAsync());
        db.OriginalAssignments.AddRange(seats.Select(a => new OriginalAssignmentEntity { FairSlotId = slot.Id, BallotId = a.BallotId, SessionId = a.SessionId }));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Demo: two of three sample people tick the activities they gave more points than their seat, so seats given back move up.
    /// Never an activity they gave the maximum, so a demo person who did moves up first.
    /// </summary>
    private static async Task TickWaitlistsAsync(AppDbContext db, FairSlotEntity slot)
    {
        var rnd = new Random(slot.Seed + 2);
        var (allocation, _, _) = await AllocationEndpoints.CurrentAsync(db, slot);
        foreach (var b in await db.Ballots.Include(b => b.Points).Where(b => b.FairSlotId == slot.Id && b.IsSample).ToListAsync())
        {
            if (rnd.Next(3) == 0) continue;
            var lowest = allocation.Assignments[b.Id.ToString()].Select(x => x.Points).DefaultIfEmpty(0).Min();
            b.Waitlist = b.Points.Where(p => p.Points > lowest && p.Points < slot.MaxPerActivity && allocation.Assignments[b.Id.ToString()].All(x => x.Session.ActivityId != p.ActivityId))
                .Select(p => p.ActivityId).ToList();
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Creates the database rows for a model activity (new ids for the activity and its sessions).</summary>
    public static ActivityEntity ToEntity(Activity a, Guid slotId, int sortOrder)
    {
        var entity = new ActivityEntity
        {
            FairSlotId = slotId, Title = a.Title, RestrictedTo = a.RestrictedTo, Popularity = a.Popularity,
            GroupId = a.GroupId, Description = a.Description, Parish = a.Parish, Leader = a.Leader, LeaderEmail = a.LeaderEmail, SortOrder = sortOrder,
        };
        entity.Sessions = a.Sessions.Select(s => new SessionEntity
        {
            ActivityId = entity.Id, Start = s.Start, End = s.End, Location = s.Location, Capacity = s.Capacity, Flexible = s.Flexible,
        }).ToList();
        return entity;
    }

    /// <summary>Whether a person may see and vote in a FairSlot: listed, or the FairSlot is open to all and the person is not excluded (demo person A neither).</summary>
    public static async Task<bool> CanTakePartAsync(AppDbContext db, Guid slotId, string name)
    {
        var open = await db.FairSlots.Where(s => s.Id == slotId && s.OpenToAll).Select(s => s.Excluded).FirstOrDefaultAsync();
        if (open is not null && OpenFor(open, name)) return true;
        return await IsParticipantAsync(db, slotId, name);
    }

    /// <summary>Whether a FairSlot open to all lets this person in, given its exclusion list.</summary>
    public static bool OpenFor(IEnumerable<string> excluded, string name)
    {
        var n = name.Trim();
        return n.Length > 0 && !n.Equals(DemoData.NobodyName, StringComparison.OrdinalIgnoreCase)
            && !excluded.Any(x => x.Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a person (by name) is on the participant list of a FairSlot.</summary>
    public static Task<bool> IsParticipantAsync(AppDbContext db, Guid slotId, string name)
    {
        var n = name.Trim().ToLower();
        return db.Participants.AnyAsync(p => p.FairSlotId == slotId && p.Name.ToLower() == n);
    }

    private static async Task WipeAsync(AppDbContext db)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
        else
            await db.Database.EnsureDeletedAsync();
    }

    /// <summary>Replaces the sample ballots of a FairSlot according to its current rules. A frozen allocation is calculated and stored anew.</summary>
    public static async Task ResetSamplesAsync(AppDbContext db, Guid slotId, bool removeRealBallots = false)
    {
        var slot = await db.FairSlots.SingleAsync(s => s.Id == slotId);
        db.Ballots.RemoveRange(await db.Ballots.Where(b => b.FairSlotId == slotId && (removeRealBallots || b.IsSample)).ToListAsync());

        var activities = await LoadActivitiesAsync(db, slotId);
        if (activities.Count > 0)
        {
            foreach (var b in SampleData.Participants(slot.SampleCount, slot.ToRules(), activities, slot.Seed))
            {
                db.Ballots.Add(new BallotEntity
                {
                    FairSlotId = slotId, Name = b.Name, Gender = b.Gender, IsSample = true,
                    Points = b.Points.Select(p => new BallotPoints { ActivityId = p.Key, Points = p.Value }).ToList(),
                    UnavailableSessions = b.UnavailableSessions.ToList(),
                });
            }
        }
        await db.SaveChangesAsync();
        if (slot.FrozenAt is null) return;
        // A fresh allocation: seats given back count again.
        foreach (var b in await db.Ballots.Where(b => b.FairSlotId == slotId).ToListAsync())
        {
            b.Declined = [];
            b.NoReplacement = [];
        }
        await db.SaveChangesAsync();
        await AllocationEndpoints.FreezeAsync(db, slot);
        if (slot.Phase == Phase.Published) await TickWaitlistsAsync(db, slot);
        // A new start: older save points refer to ballots that are gone.
        AllocationEndpoints.Log(db, slotId, Phases.Clock, "frozen");
        await db.SaveChangesAsync();
    }

    public static async Task<List<Activity>> LoadActivitiesAsync(AppDbContext db, Guid slotId) =>
        (await db.Activities.Include(a => a.Sessions).AsNoTracking()
            .Where(a => a.FairSlotId == slotId).OrderBy(a => a.SortOrder).ToListAsync())
        .Select(a => a.ToModel()).ToList();

    public static async Task<List<BallotEntity>> LoadBallotsAsync(AppDbContext db, Guid slotId) =>
        await db.Ballots.Include(b => b.Points).AsNoTracking().Where(b => b.FairSlotId == slotId).ToListAsync();
}
