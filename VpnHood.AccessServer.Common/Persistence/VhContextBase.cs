using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Persistence;

public abstract class VhContextBase : DbContext
{
    public const int MaxDescriptionLength = 1000;

    public virtual DbSet<Project> Projects { get; set; } = default!;
    public virtual DbSet<AccessToken> AccessTokens { get; set; } = default!;
    public virtual DbSet<Access> Accesses { get; set; } = default!;
    public virtual DbSet<Device> Devices { get; set; } = default!;
    public virtual DbSet<PublicCycle> PublicCycles { get; set; } = default!;
    public virtual DbSet<Models.Server> Servers { get; set; } = default!;
    public virtual DbSet<ServerStatusEx> ServerStatuses { get; set; } = default!;
    public virtual DbSet<AccessPoint> AccessPoints { get; set; } = default!;
    public virtual DbSet<AccessPointGroup> AccessPointGroups { get; set; } = default!;
    public virtual DbSet<Session> Sessions { get; set; } = default!;
    public virtual DbSet<AccessUsageEx> AccessUsages { get; set; } = default!;
    public virtual DbSet<Certificate> Certificates { get; set; } = default!;
    public virtual DbSet<IpLock> IpLocks { get; set; } = default!;

    protected VhContextBase(DbContextOptions options)
        : base(options)
    {
    }

    public async Task<IDbContextTransaction?> WithNoLockTransaction()
    {
        return Database.CurrentTransaction == null ? await Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted) : null;
    }

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HavePrecision(0);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_100_CI_AS_SC_UTF8");

        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(e => e.ProjectId);
        });

        modelBuilder.Entity<IpLock>(entity =>
        {
            entity.HasKey(e => new { e.ProjectId, e.IpAddress });
            entity.Property(e => e.IpAddress)
                .HasMaxLength(40);

            entity.Property(e => e.Description)
                .HasMaxLength(MaxDescriptionLength);
        });

        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.Property(e => e.CommonName)
                .HasMaxLength(200);
        });

        modelBuilder.Entity<AccessToken>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.SupportCode })
                .IsUnique();

            entity.Property(e => e.AccessTokenName)
                .HasMaxLength(50);

            entity.Property(e => e.Secret)
                .HasMaxLength(16)
                .IsFixedLength();

            entity.Property(e => e.Url)
                .HasMaxLength(255);

            entity.HasOne(e => e.Project)
                .WithMany(d => d.AccessTokens)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.ClientId })
                .IsUnique();

            entity.HasIndex(e => new { e.ProjectId, e.CreatedTime });
            entity.HasIndex(e => new { e.ProjectId, e.ModifiedTime });

            entity.Property(e => e.IpAddress)
                .HasMaxLength(50);

            entity.Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity.Property(e => e.UserAgent)
                .HasMaxLength(500);

            entity.HasOne(e => e.Project)
                .WithMany(d => d.Devices)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PublicCycle>(entity =>
        {
            entity.Property(e => e.PublicCycleId)
                .HasMaxLength(12)
                .IsFixedLength();
        });

        modelBuilder.Entity<Models.Server>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.ServerName })
                .HasFilter($"{nameof(Models.Server.ServerName)} IS NOT NULL")
                .IsUnique();

            entity.Property(e => e.LogClientIp)
                .HasDefaultValue(false);

            entity.Property(e => e.LogLocalPort)
                .HasDefaultValue(false);

            entity.Property(e => e.Description)
                .HasMaxLength(400);

            entity.Property(e => e.ServerName)
                .HasMaxLength(100);

            entity.Property(e => e.OsInfo)
                .HasMaxLength(500);

            entity.Property(e => e.Version)
                .HasMaxLength(100);

            entity.Property(e => e.EnvironmentVersion)
                .HasMaxLength(100);

            entity.Property(e => e.MachineName)
                .HasMaxLength(100);

            entity.Property(e => e.LastConfigError)
                .HasMaxLength(2000);

            entity.Property(e => e.Secret)
                .HasMaxLength(32);

            entity.Ignore(e => e.ServerStatus);
        });

        modelBuilder.Entity<ServerStatusEx>(entity =>
        {
            entity
                .ToTable(nameof(ServerStatuses))
                .HasKey(x => x.ServerStatusId);

            // for cleanup maintenance
            entity
                .HasIndex(e => new { e.ServerStatusId })
                .HasFilter($"{nameof(ServerStatusEx.IsLast)} = 0");

            entity
                .HasIndex(e => new { e.ProjectId, e.ServerId, e.IsLast })
                .IncludeProperties(e => new
                {
                    e.IsConfigure,
                    e.SessionCount,
                    e.TunnelSendSpeed,
                    e.TunnelReceiveSpeed,
                    e.CreatedTime,
                    e.TcpConnectionCount,
                    e.UdpConnectionCount,
                    e.ThreadCount,
                    e.FreeMemory
                })
                .IsUnique()
                .HasFilter($"{nameof(ServerStatusEx.IsLast)} = 1");

            entity
                .Property(e => e.ServerStatusId)
                .ValueGeneratedOnAdd();

            entity.HasOne(e => e.Project)
                .WithMany(d => d.ServerStatuses)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Session>(entity =>
        {
            //index for finding other active sessions of an AccessId
            entity.HasIndex(e => e.AccessId)
                .HasFilter($"{nameof(Session.EndTime)} IS NULL");

            entity.HasIndex(e => new { e.EndTime }); //for sync 

            entity.Property(e => e.SessionId)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.DeviceIp)
                .HasMaxLength(50);

            entity.Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity.HasOne(e => e.Server)
                .WithMany(d => d.Sessions)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Ignore(e => e.IsEndTimeSaved);
        });


        modelBuilder.Entity<AccessPoint>(entity =>
        {
            entity.Property(e => e.IpAddress)
                .HasMaxLength(40);

            entity.HasIndex(e => new { e.ServerId, e.IpAddress, e.TcpPort, e.IsListen })
                .IsUnique();

            entity.HasOne(e => e.AccessPointGroup)
                .WithMany(d => d.AccessPoints)
                .HasForeignKey(e => e.AccessPointGroupId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AccessPointGroup>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.AccessPointGroupName })
                .IsUnique();

            entity.Property(e => e.AccessPointGroupName)
                .HasMaxLength(100);

            entity.HasOne(e => e.Project)
                .WithMany(d => d.AccessPointGroups)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Access>(entity =>
        {
            entity.HasIndex(e => new { e.AccessTokenId, e.DeviceId })
                .IsUnique()
                .HasFilter(null); //required to prevent EF created filtered index

            entity.Property(e => e.Description)
                .HasMaxLength(MaxDescriptionLength);

            entity.Property(e => e.LastCycleTraffic)
                .HasComputedColumnSql($"{nameof(Access.LastCycleSentTraffic)} + {nameof(Access.LastCycleReceivedTraffic)} - {nameof(Access.LastCycleSentTraffic)} - {nameof(Access.LastCycleReceivedTraffic)}");


            entity.HasIndex(e => new { e.CycleTraffic }); // for resetting cycles

            entity.Property(e => e.CycleTraffic)
                .HasComputedColumnSql($"{nameof(Access.TotalSentTraffic)} + {nameof(Access.TotalReceivedTraffic)} - {nameof(Access.LastCycleSentTraffic)} - {nameof(Access.LastCycleReceivedTraffic)}");

            entity.Property(e => e.TotalTraffic)
                .HasComputedColumnSql($"{nameof(Access.TotalSentTraffic)} + {nameof(Access.TotalReceivedTraffic)}");

            entity.HasOne(e => e.Device)
                .WithMany(d => d.Accesses)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AccessUsageEx>(entity =>
        {
            entity
                .HasKey(x => x.AccessUsageId);

            entity
                .Property(e => e.AccessUsageId)
                .ValueGeneratedOnAdd();

            entity
                .HasOne(e => e.Session)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.Server)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.AccessPointGroup)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.AccessPointGroupId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.AccessToken)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.AccessTokenId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.Device)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}