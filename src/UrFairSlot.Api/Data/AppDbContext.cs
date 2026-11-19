using Microsoft.EntityFrameworkCore;

namespace UrFairSlot.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<FairSlotEntity> FairSlots => Set<FairSlotEntity>();
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<BallotEntity> Ballots => Set<BallotEntity>();
    public DbSet<ParticipantEntity> Participants => Set<ParticipantEntity>();
    public DbSet<MetaEntity> Meta => Set<MetaEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FairSlotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Activities).WithOne().HasForeignKey(a => a.FairSlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Ballots).WithOne().HasForeignKey(a => a.FairSlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(a => a.FairSlotId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ParticipantEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.FairSlotId, x.Name });
        });
        b.Entity<ActivityEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasMany(x => x.Sessions).WithOne().HasForeignKey(s => s.ActivityId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SessionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.Location).HasMaxLength(200);
        });
        b.Entity<BallotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.FairSlotId, x.IsSample });
            e.OwnsMany(x => x.Points, p =>
            {
                p.WithOwner().HasForeignKey("BallotId");
                p.HasKey("BallotId", nameof(BallotPoints.ActivityId));
            });
        });
        b.Entity<MetaEntity>().HasKey(x => x.Id);
    }
}
