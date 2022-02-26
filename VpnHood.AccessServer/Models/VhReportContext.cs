using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

#nullable disable
namespace VpnHood.AccessServer.Models;

// ReSharper disable once PartialTypeWithSinglePart
public partial class VhReportContext : DbContext
{
    private IDbContextTransaction _transaction;

    public bool DebugMode { get; set; } = false;
    public virtual DbSet<ServerStatusEx> ServerStatuses { get; set; }
    public virtual DbSet<AccessUsageEx> AccessUsages { get; set; }
    public virtual DbSet<Session> Sessions { get; set; }

    public VhReportContext()
    {
    }

    public VhReportContext(DbContextOptions<VhReportContext> options)
        : base(options)
    {
    }

    public async Task<VhReportContext> WithNoLock()
    {
        _transaction = await Database.BeginTransactionAsync();
        Database.SetCommandTimeout(600);
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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        if (optionsBuilder.IsConfigured) return;
        optionsBuilder.UseSqlServer(AccessServerApp.Instance.ReportConnectionString);
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

        modelBuilder.Entity<ServerStatusEx>(entity =>
        {
            entity
                .ToTable(nameof(ServerStatuses))
                .HasKey(e => e.ServerStatusId);

            entity.Property(e => e.ServerStatusId)
                .ValueGeneratedNever();

            entity
                .HasIndex(e => new { e.ProjectId, e.CreatedTime })
                .IncludeProperties(e => new { e.ServerId, e.SessionCount, e.TunnelSendSpeed, e.TunnelReceiveSpeed });

            entity
                .HasIndex(e => new { e.ServerId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionCount, e.TunnelSendSpeed, e.TunnelReceiveSpeed });

            entity.Ignore(x => x.Project);
            entity.Ignore(x => x.Server);
            entity.Ignore(x => x.IsLast);
        });

        modelBuilder.Entity<AccessUsageEx>(entity =>
        {
            entity
                .ToTable(nameof(AccessUsages))
                .HasKey(x => x.AccessUsageId);

            entity.Property(e => e.AccessUsageId)
                .ValueGeneratedNever();

            entity.HasIndex(e => new { e.ProjectId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic, e.AccessId, e.AccessPointGroupId });

            entity.HasIndex(e => new { e.ProjectId, e.AccessId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.ProjectId, e.AccessPointGroupId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.ProjectId, e.AccessTokenId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.ProjectId, e.ServerId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.ProjectId, e.DeviceId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.SentTraffic, e.ReceivedTraffic });

            entity.Ignore(e => e.Access);
            entity.Ignore(e => e.Session);
            entity.Ignore(e => e.Server);
            entity.Ignore(e => e.Device);
            entity.Ignore(e => e.Project);
            entity.Ignore(e => e.AccessPointGroup);
            entity.Ignore(e => e.AccessToken);
        });


        modelBuilder.Entity<Session>(entity =>
        {
            entity.Property(e => e.SessionId)
                .ValueGeneratedNever();

            entity.Property(e => e.DeviceIp)
                .HasMaxLength(50);

            entity.Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity.Ignore(e => e.Server);
            entity.Ignore(e => e.Device);
            entity.Ignore(e => e.Access);
            entity.Ignore(e => e.AccessToken);
            entity.Ignore(e => e.AccessUsages);
        });

        // ReSharper disable once InvocationIsSkipped
        OnModelCreatingPartial(modelBuilder);
    }

    // ReSharper disable once PartialMethodWithSinglePart
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}