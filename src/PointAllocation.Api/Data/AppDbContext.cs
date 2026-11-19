using Microsoft.EntityFrameworkCore;

namespace PointAllocation.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<BallotEntity> Ballots => Set<BallotEntity>();
    public DbSet<SettingsEntity> Settings => Set<SettingsEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ActivityEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasMany(x => x.Sessions).WithOne().HasForeignKey(s => s.ActivityId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SessionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Location).HasMaxLength(200);
        });
        b.Entity<BallotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => x.IsSample);
            e.OwnsMany(x => x.Points, p =>
            {
                p.WithOwner().HasForeignKey("BallotId");
                p.HasKey("BallotId", nameof(BallotPoints.ActivityId));
            });
        });
        b.Entity<SettingsEntity>().HasKey(x => x.Id);
    }
}
