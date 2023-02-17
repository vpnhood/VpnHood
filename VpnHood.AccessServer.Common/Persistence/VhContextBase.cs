using System;
using System.Data;
using System.Reflection.Metadata;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.Server.Configurations;

namespace VpnHood.AccessServer.Persistence;

public abstract class VhContextBase : DbContext
{
    public const int MaxDescriptionLength = 1000;

    public virtual DbSet<ProjectModel> Projects { get; set; } = default!;
    public virtual DbSet<AccessTokenModel> AccessTokens { get; set; } = default!;
    public virtual DbSet<AccessModel> Accesses { get; set; } = default!;
    public virtual DbSet<DeviceModel> Devices { get; set; } = default!;
    public virtual DbSet<PublicCycleModel> PublicCycles { get; set; } = default!;
    public virtual DbSet<ServerModel> Servers { get; set; } = default!;
    public virtual DbSet<ServerStatusModel> ServerStatuses { get; set; } = default!;
    public virtual DbSet<AccessPointModel> AccessPoints { get; set; } = default!;
    public virtual DbSet<AccessPointGroupModel> AccessPointGroups { get; set; } = default!;
    public virtual DbSet<SessionModel> Sessions { get; set; } = default!;
    public virtual DbSet<AccessUsageModel> AccessUsages { get; set; } = default!;
    public virtual DbSet<CertificateModel> Certificates { get; set; } = default!;
    public virtual DbSet<IpLockModel> IpLocks { get; set; } = default!;
    public virtual DbSet<ServerProfileModel> ServerProfiles { get; set; } = default!;

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

        configurationBuilder.Properties<string>()
            .HaveMaxLength(4000);

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_100_CI_AS_SC_UTF8");

        modelBuilder.Entity<ProjectModel>(entity =>
        {
            entity.HasKey(e => e.ProjectId);

            entity.Property(e => e.GaTrackId)
                .HasMaxLength(50);

            entity.Property(e => e.ProjectName)
                .HasMaxLength(200);
        });

        modelBuilder.Entity<IpLockModel>(entity =>
        {
            entity.HasKey(e => new { e.ProjectId, e.IpAddress });

            entity.Property(e => e.IpAddress)
                .HasMaxLength(40);

            entity.Property(e => e.Description)
                .HasMaxLength(MaxDescriptionLength);
        });

        modelBuilder.Entity<CertificateModel>(entity =>
        {
            entity.HasKey(e => e.CertificateId);

            entity.Property(e => e.CommonName)
                .HasMaxLength(200);
        });

        modelBuilder.Entity<AccessTokenModel>(entity =>
        {
            entity.HasKey(e => e.AccessTokenId);

            entity.HasIndex(e => new { e.ProjectId, e.SupportCode })
                .IsUnique();

            entity.Property(e => e.AccessTokenName)
                .HasMaxLength(50);

            entity.Property(e => e.Secret)
                .HasMaxLength(16)
                .IsFixedLength();

            entity.Property(e => e.Url)
                .HasMaxLength(255);

            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false);

            entity.HasOne(e => e.Project)
                .WithMany(d => d.AccessTokens)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DeviceModel>(entity =>
        {
            entity.HasKey(e => e.DeviceId);

            entity.HasIndex(e => new { e.ProjectId, e.ClientId })
                .IsUnique();

            entity.HasIndex(e => new { e.ProjectId, e.CreatedTime });
            entity.HasIndex(e => new { e.ProjectId, e.ModifiedTime });

            entity.Property(e => e.IpAddress)
                .HasMaxLength(50);

            entity.Property(e => e.Country)
                .HasMaxLength(10);

            entity.Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity.Property(e => e.UserAgent)
                .HasMaxLength(500);

            entity.HasOne(e => e.Project)
                .WithMany(d => d.Devices)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PublicCycleModel>(entity =>
        {
            entity.HasKey(e => e.PublicCycleId);

            entity.Property(e => e.PublicCycleId)
                .HasMaxLength(12)
                .IsFixedLength();
        });

        modelBuilder.Entity<ServerModel>(entity =>
        {
            entity.HasKey(e => e.ServerId);

            entity.HasIndex(e => new { e.ProjectId, e.ServerName })
                .HasFilter($"{nameof(ServerModel.ServerName)} IS NOT NULL and IsDeleted = 0")
                .IsUnique();

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

            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false);

            entity.Ignore(e => e.ServerStatus);
        });

        modelBuilder.Entity<ServerStatusModel>(entity =>
        {
            entity.HasKey(e => e.ServerStatusId);

            entity
                .Property(e => e.ServerStatusId)
                .ValueGeneratedOnAdd();

            entity
                .ToTable(nameof(ServerStatuses))
                .HasKey(x => x.ServerStatusId);

            // for cleanup maintenance
            entity
                .HasIndex(e => new { e.ServerStatusId })
                .HasFilter($"{nameof(ServerStatusModel.IsLast)} = 0");

            entity
                .HasIndex(e => new { e.ProjectId, e.ServerId, e.IsLast })
                .IncludeProperties(e => new
                {
                    e.SessionCount,
                    e.TcpConnectionCount,
                    e.UdpConnectionCount,
                    e.AvailableMemory,
                    e.CpuUsage,
                    e.ThreadCount,
                    e.TunnelSendSpeed,
                    e.TunnelReceiveSpeed,
                    e.IsConfigure,
                    e.CreatedTime,
                })
                .IsUnique()
                .HasFilter($"{nameof(ServerStatusModel.IsLast)} = 1");

            entity
                .HasIndex(e => new { e.CreatedTime })
                .HasFilter($"{nameof(ServerStatusModel.IsLast)} = 1");

            entity.HasOne(e => e.Project)
                .WithMany(d => d.ServerStatuses)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SessionModel>(entity =>
        {
            entity.HasKey(e => e.SessionId);

            //index for finding other active sessions of an AccessId
            entity.HasIndex(e => e.AccessId)
                .HasFilter($"{nameof(SessionModel.EndTime)} IS NULL");

            entity.HasIndex(e => new { e.EndTime }); //for sync 

            entity.Property(e => e.IsArchived);

            entity.Property(e => e.SessionId)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.DeviceIp)
                .HasMaxLength(50);

            entity.Property(e => e.Country)
                .HasMaxLength(10);

            entity.Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity.HasOne(e => e.Server)
                .WithMany(d => d.Sessions)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.NoAction);
        });


        modelBuilder.Entity<AccessPointModel>(entity =>
        {
            entity.HasKey(e => e.AccessPointId);

            entity.Property(e => e.IpAddress)
                .HasMaxLength(40);

            entity.HasIndex(e => new { e.ServerId, e.IpAddress, e.TcpPort, e.IsListen })
                .IsUnique();

            entity.HasOne(e => e.AccessPointGroup)
                .WithMany(d => d.AccessPoints)
                .HasForeignKey(e => e.AccessPointGroupId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AccessPointGroupModel>(entity =>
        {
            entity.HasKey(e => e.AccessPointGroupId);

            entity.HasIndex(e => new { e.ProjectId, e.AccessPointGroupName })
                .IsUnique();

            entity.Property(e => e.AccessPointGroupName)
                .HasMaxLength(100);

            entity.HasOne(e => e.Project)
                .WithMany(d => d.AccessPointGroups)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.ServerProfile)
                .WithMany(d => d.AccessPointGroups)
                .HasForeignKey(e => new { e.ProjectId, e.ServerProfileId })
                .HasPrincipalKey(e => new { e.ProjectId, e.ServerProfileId })
                .OnDelete(DeleteBehavior.NoAction);

        });

        modelBuilder.Entity<AccessModel>(entity =>
        {
            entity.HasKey(e => e.AccessId);

            entity.HasIndex(e => new { e.AccessTokenId, e.DeviceId })
                .IsUnique()
                .HasFilter(null); //required to prevent EF created filtered index

            entity.Property(e => e.Description)
                .HasMaxLength(MaxDescriptionLength);

            entity.Property(e => e.LastCycleTraffic)
                .HasComputedColumnSql($"{nameof(AccessModel.LastCycleSentTraffic)} + {nameof(AccessModel.LastCycleReceivedTraffic)} - {nameof(AccessModel.LastCycleSentTraffic)} - {nameof(AccessModel.LastCycleReceivedTraffic)}");


            entity.HasIndex(e => new { e.CycleTraffic }); // for resetting cycles

            entity.Property(e => e.CycleTraffic)
                .HasComputedColumnSql($"{nameof(AccessModel.TotalSentTraffic)} + {nameof(AccessModel.TotalReceivedTraffic)} - {nameof(AccessModel.LastCycleSentTraffic)} - {nameof(AccessModel.LastCycleReceivedTraffic)}");

            entity.Property(e => e.TotalTraffic)
                .HasComputedColumnSql($"{nameof(AccessModel.TotalSentTraffic)} + {nameof(AccessModel.TotalReceivedTraffic)}");

            entity.HasOne(e => e.Device)
                .WithMany(d => d.Accesses)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AccessUsageModel>(entity =>
        {
            entity
                .HasKey(x => x.AccessUsageId);

            entity
                .Property(e => e.AccessUsageId)
                .ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<ServerProfileModel>(entity =>
        {
            entity.HasKey(x => x.ServerProfileId);
        });
    }
}
