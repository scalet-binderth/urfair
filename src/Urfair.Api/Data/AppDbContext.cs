using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Urfair.Api.Data;

public class AppDbContext(DbContextOptions options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<FairSlotEntity> FairSlots => Set<FairSlotEntity>();
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<BallotEntity> Ballots => Set<BallotEntity>();
    public DbSet<ParticipantEntity> Participants => Set<ParticipantEntity>();
    public DbSet<MailEntity> Mails => Set<MailEntity>();
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<AssignmentEntity> Assignments => Set<AssignmentEntity>();
    public DbSet<OriginalAssignmentEntity> OriginalAssignments => Set<OriginalAssignmentEntity>();
    public DbSet<AllocationLogEntity> AllocationLog => Set<AllocationLogEntity>();
    public DbSet<AllocationSnapshotEntity> AllocationSnapshots => Set<AllocationSnapshotEntity>();
    /// <summary>Keys for the sign-in cookies, so they stay valid when the server restarts.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>After a change to seats has been saved, a save point with the new state is stored (see <see cref="SavePoints"/>).</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var points = SavePoints.Pending(this);
        var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (points.Count == 0) return count;
        foreach (var (slotId, logId) in points) await SavePoints.AddAsync(this, slotId, logId, cancellationToken);
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        return count;
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FairSlotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Parish).HasMaxLength(120);
            e.Property(x => x.Organiser).HasMaxLength(120);
            e.Property(x => x.OrganiserEmail).HasMaxLength(200);
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20).HasDefaultValue(Visibility.JuniaShared);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Activities).WithOne().HasForeignKey(a => a.FairSlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Ballots).WithOne().HasForeignKey(a => a.FairSlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(a => a.FairSlotId).OnDelete(DeleteBehavior.Cascade);
            e.OwnsMany(x => x.Groups, g =>
            {
                g.ToTable("Groups");
                g.WithOwner().HasForeignKey("FairSlotId");
                g.HasKey("FairSlotId", nameof(GroupEntity.Id));
                g.Property(x => x.Id).HasMaxLength(40);
                g.Property(x => x.Name).HasMaxLength(100);
                g.Property(x => x.Color).HasMaxLength(20);
            });
        });
        b.Entity<ParticipantEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.FairSlotId, x.Name });
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Parish).HasMaxLength(120);
            e.Property(x => x.Token).HasMaxLength(40);
            e.HasIndex(x => x.Token); // not unique: rows from before tokens start empty and are filled at start-up
        });
        b.Entity<AccountEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.SetupToken).HasMaxLength(40);
        });
        b.Entity<MailEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.To).HasMaxLength(200);
            e.Property(x => x.ReplyTo).HasMaxLength(200);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => new { x.Outbox, x.SentAt });
            e.HasIndex(x => x.FairSlotId);
            e.HasOne<FairSlotEntity>().WithMany().HasForeignKey(x => x.FairSlotId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AssignmentEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SessionId).HasMaxLength(40);
            e.HasIndex(x => x.FairSlotId);
            e.HasOne<FairSlotEntity>().WithMany().HasForeignKey(x => x.FairSlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BallotEntity>().WithMany().HasForeignKey(x => x.BallotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SessionEntity>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<OriginalAssignmentEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SessionId).HasMaxLength(40);
            e.HasIndex(x => x.FairSlotId);
            e.HasOne<FairSlotEntity>().WithMany().HasForeignKey(x => x.FairSlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BallotEntity>().WithMany().HasForeignKey(x => x.BallotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SessionEntity>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AllocationLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Actor).HasMaxLength(120);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Person).HasMaxLength(120);
            e.Property(x => x.Before).HasMaxLength(1000);
            e.Property(x => x.After).HasMaxLength(1000);
            e.HasIndex(x => x.FairSlotId);
            e.HasOne<FairSlotEntity>().WithMany().HasForeignKey(x => x.FairSlotId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AllocationSnapshotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.FairSlotId, x.LogId });
            e.HasOne<FairSlotEntity>().WithMany().HasForeignKey(x => x.FairSlotId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ActivityEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.GroupId).HasMaxLength(40);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Parish).HasMaxLength(120);
            e.Property(x => x.Leader).HasMaxLength(120);
            e.Property(x => x.LeaderEmail).HasMaxLength(200);
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
    }
}
