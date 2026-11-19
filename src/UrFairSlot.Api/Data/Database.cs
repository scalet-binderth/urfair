using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using UrFairSlot.Core;

namespace UrFairSlot.Api.Data;

public static class Database
{
    /// <summary>
    /// Bump when the model changes. While the app only holds demo data, a version mismatch resets the
    /// database instead of migrating it. Switch to EF Core migrations before real registrations are stored.
    /// </summary>
    private const int SchemaVersion = 2;

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

        builder.Services.AddDbContext<AppDbContext>(o =>
        {
            if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
                o.UseNpgsql(string.IsNullOrEmpty(url) ? connection : FromUrl(url));
            else
                o.UseSqlite(connection ?? "Data Source=urfairslot.db");
        });
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

    /// <summary>Creates (or, on a schema change, recreates) the database and adds a demo FairSlot if there is none.</summary>
    public static async Task InitialiseAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var creator = db.GetService<IRelationalDatabaseCreator>();

        if (await creator.ExistsAsync() && await creator.HasTablesAsync() && await CurrentVersionAsync(db) != SchemaVersion)
        {
            logger.LogWarning("Database schema changed: recreating the (demo) database.");
            await WipeAsync(db);
        }

        await db.Database.EnsureCreatedAsync();
        if (!await db.Meta.AnyAsync())
        {
            db.Meta.Add(new MetaEntity { SchemaVersion = SchemaVersion });
            await db.SaveChangesAsync();
        }

        if (!await db.FairSlots.AnyAsync())
            await CreateDemoSlotAsync(db, "Firmvorbereitung Urfahr 2026/27 (Demo)", Phase.Open, 140);
    }

    private static async Task<int?> CurrentVersionAsync(AppDbContext db)
    {
        try { return await db.Meta.Where(m => m.Id == 1).Select(m => (int?)m.SchemaVersion).SingleOrDefaultAsync(); }
        catch { return null; } // table missing: database from before versioning
    }

    private static async Task WipeAsync(AppDbContext db)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
        else
            await db.Database.EnsureDeletedAsync();
    }

    /// <summary>Creates a FairSlot filled with the Urfahr sample activities and sample participants.</summary>
    public static async Task<FairSlotEntity> CreateDemoSlotAsync(AppDbContext db, string name, Phase phase, int sampleCount)
    {
        var slot = new FairSlotEntity
        {
            Name = name, Phase = phase, SampleCount = sampleCount,
            RegistrationDeadline = new DateTime(2026, 11, 18, 23, 59, 0),
        };
        var order = 0;
        foreach (var a in SampleData.Activities())
        {
            var activity = new ActivityEntity { FairSlotId = slot.Id, Title = a.Title, RestrictedTo = a.RestrictedTo, Popularity = a.Popularity, SortOrder = order++ };
            activity.Sessions = a.Sessions.Select(s => new SessionEntity
            {
                ActivityId = activity.Id, Start = s.Start, End = s.End, Location = s.Location, Capacity = s.Capacity, Flexible = s.Flexible,
            }).ToList();
            slot.Activities.Add(activity);
        }
        db.FairSlots.Add(slot);
        await db.SaveChangesAsync();
        await ResetSamplesAsync(db, slot.Id);
        return slot;
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
