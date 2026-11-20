using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Urfair.Api.Data;

// EF Core migrations are specific to a database provider, so each provider gets its own context type
// and its own migrations folder. The model itself lives in AppDbContext.
// New migration (run both):
//   dotnet ef migrations add <Name> -p src/Urfair.Api -c SqliteAppDbContext -o Data/Migrations/Sqlite
//   dotnet ef migrations add <Name> -p src/Urfair.Api -c PostgresAppDbContext -o Data/Migrations/Postgres

public class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options);

public class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : AppDbContext(options);

/// <summary>Used only by the dotnet-ef tool when creating migrations.</summary>
public class SqliteDesignFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design.db").Options);
}

/// <summary>Used only by the dotnet-ef tool when creating migrations; no connection is opened.</summary>
public class PostgresDesignFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true); // same as at runtime
        return new(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=localhost;Database=design").Options);
    }
}
