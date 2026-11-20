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
    /// Five demo FairSlots for presentations. Demo people: A sees nothing, B two open FairSlots,
    /// C two open and one published, D two open, one closed and one published.
    /// B, C and D have already voted in their open FairSlots and can change their ballots.
    /// "Pfarrfest" has no sample ballots, to try voting live.
    /// </summary>
    private static async Task SeedDemoAsync(AppDbContext db)
    {
        var (b, c, d) = (DemoData.B.Name, DemoData.C.Name, DemoData.D.Name);
        await CreateSlotAsync(db, "Firmvorbereitung Urfahr 2026/27 (Demo)", Phase.Open, new DateTime(2026, 11, 18, 23, 59, 0),
            SampleData.FirmungRules, SampleData.Activities(), 140, [b, c, d], withBallots: [b, c, d], parish: SampleData.FirmungParish, organiser: SampleData.FirmungOrganiser, visibility: Visibility.JuniaReadOnly);
        await CreateSlotAsync(db, "Ministrantenausflug Herbst 2026", Phase.Published, new DateTime(2026, 9, 15, 23, 59, 0),
            DemoData.ExcursionRules, DemoData.Excursion(), 40, [c, d], withBallots: [c, d], parish: "Pfarrgemeinde Urfahr-Hl. Geist", organiser: "Miriam Koller", visibility: Visibility.ParishOnly);
        await CreateSlotAsync(db, "Ferienprogramm Sommer 2026", Phase.Closed, new DateTime(2026, 6, 30, 23, 59, 0),
            DemoData.HolidayRules, DemoData.HolidayProgramme(), 60, [d], withBallots: [d], parish: "Pfarrgemeinde Urfahr-St. Magdalena", organiser: "Stefan Huber");
        await CreateSlotAsync(db, "Jungschar-Sommerlager 2027", Phase.Draft, new DateTime(2027, 5, 31, 23, 59, 0),
            DemoData.CampRules, DemoData.Camp(), 0, [], parish: "Pfarrgemeinde Urfahr-Christkönig", organiser: "Jungschar Christkönig", visibility: Visibility.ParishOnly);
        await CreateSlotAsync(db, "Pfarrfest 2027 – Mithelfen", Phase.Open, new DateTime(2027, 5, 31, 23, 59, 0),
            DemoData.FestivalRules, DemoData.ParishFestival(), 0, [], withBallots: [b, c, d], openToAll: true, parish: "Pfarrgemeinde Urfahr-St. Markus", organiser: "Maria Pichler", visibility: Visibility.ParishOnly);
    }

    /// <summary>Creates a FairSlot from the Urfahr sample activities (used for "create with sample data").</summary>
    public static Task<FairSlotEntity> CreateDemoSlotAsync(AppDbContext db, string name, Phase phase, int sampleCount) =>
        CreateSlotAsync(db, name, phase, new DateTime(2026, 11, 18, 23, 59, 0), SampleData.FirmungRules, SampleData.Activities(), sampleCount, [], parish: SampleData.FirmungParish, organiser: SampleData.FirmungOrganiser);

    /// <summary>Creates a FairSlot with activities, sample ballots, a participant list and optionally real ballots for some participants.</summary>
    private static async Task<FairSlotEntity> CreateSlotAsync(AppDbContext db, string name, Phase phase, DateTime deadline, Rules rules,
        IReadOnlyList<Activity> activities, int sampleCount, string[] participants, string[]? withBallots = null, bool openToAll = false,
        string? parish = null, string? organiser = null, Visibility visibility = Visibility.JuniaShared)
    {
        var slot = new FairSlotEntity { Name = name, Phase = phase, SampleCount = sampleCount, RegistrationDeadline = deadline, OpenToAll = openToAll, Parish = parish, Organiser = organiser, Visibility = visibility };
        slot.Apply(rules);
        var order = 0;
        foreach (var a in activities) slot.Activities.Add(ToEntity(a, slot.Id, order++));
        slot.Participants = participants.Select(p => DemoData.People.FirstOrDefault(x => x.Name == p) is { } d
            ? new ParticipantEntity { FairSlotId = slot.Id, Name = p, Email = d.Email, BirthDate = d.BirthDate, Parish = d.Parish }
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
                db.Ballots.Add(new BallotEntity
                {
                    FairSlotId = slot.Id, Name = withBallots[i],
                    Points = generated[i].Points.Select(p => new BallotPoints { ActivityId = p.Key, Points = p.Value }).ToList(),
                    UnavailableSessions = generated[i].UnavailableSessions.ToList(),
                });
            }
            await db.SaveChangesAsync();
        }
        return slot;
    }

    /// <summary>Creates the database rows for a model activity (new ids for the activity and its sessions).</summary>
    public static ActivityEntity ToEntity(Activity a, Guid slotId, int sortOrder)
    {
        var entity = new ActivityEntity
        {
            FairSlotId = slotId, Title = a.Title, RestrictedTo = a.RestrictedTo, Popularity = a.Popularity,
            GroupId = a.GroupId, Description = a.Description, Parish = a.Parish, Leader = a.Leader, SortOrder = sortOrder,
        };
        entity.Sessions = a.Sessions.Select(s => new SessionEntity
        {
            ActivityId = entity.Id, Start = s.Start, End = s.End, Location = s.Location, Capacity = s.Capacity, Flexible = s.Flexible,
        }).ToList();
        return entity;
    }

    /// <summary>Whether a person may see and vote in a FairSlot: listed, or the FairSlot is open to all (demo person A excluded).</summary>
    public static async Task<bool> CanTakePartAsync(AppDbContext db, Guid slotId, string name)
    {
        var n = name.Trim().ToLower();
        if (n.Length > 0 && n != DemoData.NobodyName && await db.FairSlots.AnyAsync(s => s.Id == slotId && s.OpenToAll)) return true;
        return await IsParticipantAsync(db, slotId, name);
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

    /// <summary>Replaces the sample ballots of a FairSlot according to its current rules.</summary>
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
    }

    public static async Task<List<Activity>> LoadActivitiesAsync(AppDbContext db, Guid slotId) =>
        (await db.Activities.Include(a => a.Sessions).AsNoTracking()
            .Where(a => a.FairSlotId == slotId).OrderBy(a => a.SortOrder).ToListAsync())
        .Select(a => a.ToModel()).ToList();

    public static async Task<List<BallotEntity>> LoadBallotsAsync(AppDbContext db, Guid slotId) =>
        await db.Ballots.Include(b => b.Points).AsNoTracking().Where(b => b.FairSlotId == slotId).ToListAsync();
}
