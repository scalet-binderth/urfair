using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrFairSlot.Core;

namespace UrFairSlot.Api.Data;

public static class Database
{
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

    /// <summary>Creates the schema and fills an empty database with the demo activities and sample participants.</summary>
    public static async Task InitialiseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Settings.AnyAsync())
            db.Settings.Add(new SettingsEntity { RegistrationDeadline = new DateTime(2026, 11, 18, 23, 59, 0) });

        if (!await db.Activities.AnyAsync())
        {
            var order = 0;
            foreach (var a in SampleData.Activities())
            {
                db.Activities.Add(new ActivityEntity
                {
                    Id = a.Id, Title = a.Title, RestrictedTo = a.RestrictedTo, Popularity = a.Popularity, SortOrder = order++,
                    Sessions = a.Sessions.Select(s => new SessionEntity
                    {
                        Id = s.Id, ActivityId = a.Id, Start = s.Start, End = s.End, Location = s.Location, Capacity = s.Capacity, Flexible = s.Flexible,
                    }).ToList(),
                });
            }
        }
        await db.SaveChangesAsync();

        if (!await db.Ballots.AnyAsync()) await ResetSamplesAsync(db);
    }

    /// <summary>Replaces all sample ballots according to the current settings.</summary>
    public static async Task ResetSamplesAsync(AppDbContext db, bool removeRealBallots = false)
    {
        var settings = await db.Settings.SingleAsync();
        var query = db.Ballots.Where(b => removeRealBallots || b.IsSample);
        db.Ballots.RemoveRange(await query.ToListAsync());

        var activities = (await db.Activities.Include(a => a.Sessions).ToListAsync()).Select(a => a.ToModel()).ToList();
        foreach (var b in SampleData.Participants(settings.SampleCount, settings.ToRules(), activities, settings.Seed))
        {
            db.Ballots.Add(new BallotEntity
            {
                Name = b.Name, Gender = b.Gender, IsSample = true,
                Points = b.Points.Select(p => new BallotPoints { ActivityId = p.Key, Points = p.Value }).ToList(),
                UnavailableSessions = b.UnavailableSessions.ToList(),
            });
        }
        await db.SaveChangesAsync();
    }
}
