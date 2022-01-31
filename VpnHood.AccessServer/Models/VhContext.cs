using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Authorization.Models;
using VpnHood.Common.Logging;

#nullable disable
namespace VpnHood.AccessServer.Models;

// ReSharper disable once PartialTypeWithSinglePart
public partial class VhContext : AuthDbContext
{
    private const int MaxDescriptionLength = 1000;
    private IDbContextTransaction _transaction;

    public bool DebugMode { get; set; } = false;
    public virtual DbSet<Project> Projects { get; set; }
    public virtual DbSet<ProjectRole> ProjectRoles { get; set; }
    public virtual DbSet<AccessToken> AccessTokens { get; set; }
    public virtual DbSet<Access> Accesses { get; set; }
    public virtual DbSet<Device> Devices { get; set; }
    public virtual DbSet<PublicCycle> PublicCycles { get; set; }
    public virtual DbSet<Server> Servers { get; set; }
    public virtual DbSet<ServerStatusEx> ServerStatuses { get; set; }
    public virtual DbSet<AccessPoint> AccessPoints { get; set; }
    public virtual DbSet<AccessPointGroup> AccessPointGroups { get; set; }
    public virtual DbSet<Setting> Settings { get; set; }
    public virtual DbSet<Session> Sessions { get; set; }
    public virtual DbSet<AccessUsageEx> AccessUsages { get; set; }
    public virtual DbSet<Certificate> Certificates { get; set; }
    public virtual DbSet<IpLock> IpLocks { get; set; }
    public virtual DbSet<User> Users { get; set; }

    public VhContext()
    {
    }

    public async Task<VhContext> WithNoLock()
    {
        _transaction = await Database.BeginTransactionAsync();
        return this;
    }

    public override void Dispose()
    {
        _transaction?.Dispose();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_transaction != null)
            await _transaction.DisposeAsync();

        await base.DisposeAsync();
    }

    public VhContext(DbContextOptions<VhContext> options)
        : base(options)
    {
    }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        if (optionsBuilder.IsConfigured) return;
        optionsBuilder.UseSqlServer(AccessServerApp.Instance.ConnectionString);
        if (VhLogger.IsDiagnoseMode || DebugMode)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.LogTo(x =>
            {
                if (DebugMode)
                    Debug.WriteLine(x);
            }, new[] { new EventId(20101) });
        }
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

        modelBuilder.Entity<Server>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.ServerName })
                .HasFilter($"{nameof(Server.ServerName)} IS NOT NULL")
                .IsUnique();

            entity.Property(e => e.LogClientIp)
                .HasDefaultValue(false);

            entity.Property(e => e.LogLocalPort)
                .HasDefaultValue(false);

            entity.Property(e => e.IsEnabled)
                .HasDefaultValue(true);

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

            entity.Property(e => e.Secret)
                .HasMaxLength(32);
        });

        modelBuilder.Entity<ServerStatusEx>(entity =>
        {
            entity
                .ToTable(nameof(ServerStatuses))
                .HasKey(x => x.ServerStatusId);

            // for cleanup maintenance
            entity
                .HasIndex(e => new { e.CreatedTime })
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

            entity.HasOne(e => e.AccessToken)
                .WithMany(d => d.Sessions)
                .HasForeignKey(e => e.AccessTokenId)
                .OnDelete(DeleteBehavior.NoAction);

        });


        modelBuilder.Entity<AccessPoint>(entity =>
        {
            entity.Property(e => e.IpAddress)
                .HasMaxLength(40);

            entity.HasOne(e => e.AccessPointGroup)
                .WithMany(d => d.AccessPoints)
                .HasForeignKey(e => e.AccessPointGroupId)
                .OnDelete(DeleteBehavior.NoAction);

            //entity.HasOne(e => e.Server)
            //    .WithMany(d => d.AccessPoints)
            //    .HasForeignKey(e => e.ProjectId)
            //    .OnDelete(DeleteBehavior.NoAction);

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
                .IsUnique();

            entity.Property(e => e.Description)
                .HasMaxLength(MaxDescriptionLength);

            entity.HasOne(e => e.Device)
                .WithMany(d => d.Accesses)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AccessUsageEx>(entity =>
        {
            entity
                .ToTable(nameof(AccessUsages))
                .HasKey(x => x.AccessUsageId);

            entity.HasIndex(e => new { e.AccessId, e.IsLast })
                .HasFilter($"{nameof(AccessUsageEx.IsLast)} = 1")
                .IsUnique();

            entity.HasIndex(e => new {e.CycleTotalTraffic})
                .HasFilter($"{nameof(AccessUsageEx.IsLast)} = 1");

            entity.Property(e => e.CycleTotalTraffic)
                .HasComputedColumnSql("CycleSentTraffic + CycleReceivedTraffic");
                
            entity.Property(e => e.AccessUsageId)
                .ValueGeneratedOnAdd();

            entity.HasOne(e => e.Session)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Server)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.AccessPointGroup)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.AccessPointGroupId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.AccessToken)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.AccessTokenId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Device)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Project)
                .WithMany(d => d.AccessUsages)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.UserId);
            entity.Property(e => e.AuthUserId)
                .HasMaxLength(255);

            entity.Property(e => e.UserName)
                .HasMaxLength(100);

            entity.Property(e => e.Email)
                .HasMaxLength(100);

            entity.HasIndex(e => e.Email)
                .IsUnique();

            entity.HasIndex(e => e.UserName)
                .IsUnique();
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.Property(e => e.SettingId)
                .ValueGeneratedNever();
        });

        // ReSharper disable once InvocationIsSkipped
        OnModelCreatingPartial(modelBuilder);
    }

    // ReSharper disable once PartialMethodWithSinglePart
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}