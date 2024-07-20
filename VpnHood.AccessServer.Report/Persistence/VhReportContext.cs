using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using VpnHood.AccessServer.Report.Models;

namespace VpnHood.AccessServer.Report.Persistence;

// ReSharper disable once PartialTypeWithSinglePart
public partial class VhReportContext(DbContextOptions<VhReportContext> options) : DbContext(options)
{
    public virtual DbSet<ServerStatusArchive> ServerStatuses { get; set; } = default!;
    public virtual DbSet<AccessUsageArchive> AccessUsages { get; set; } = default!;
    public virtual DbSet<SessionArchive> Sessions { get; set; } = default!;

    public async Task<IDbContextTransaction?> WithNoLockTransaction()
    {
        Database.SetCommandTimeout(600);
        return Database.CurrentTransaction == null
            ? await Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted)
            : null;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HavePrecision(0);

        configurationBuilder.Properties<string>()
            .HaveMaxLength(4000);
    }

    public static int DateDiffMinute(DateTime start, DateTime end)
    {
        throw new Exception("Should not be called!");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ServerStatusArchive>(entity =>
        {
            entity.HasKey(e => e.ServerStatusId);

            modelBuilder.HasDbFunction(() => DateDiffMinute(default, default))
                .IsBuiltIn()
                .HasTranslation(parameters =>
                    new SqlFunctionExpression("EXTRACT", parameters.Prepend(new SqlFragmentExpression("MINUTE FROM")),
                        true, new[] { false, true, true }, typeof(int), null));

            entity
                .ToTable(nameof(ServerStatuses))
                .HasKey(e => e.ServerStatusId);

            entity.Property(e => e.ServerStatusId)
                .ValueGeneratedNever();

            entity
                .Property(e => e.ServerFarmId);

            entity
                .HasIndex(e => new { e.ProjectId, e.CreatedTime })
                .IncludeProperties(e => new
                {
                    e.ServerFarmId,
                    e.ServerId,
                    e.SessionCount,
                    e.TunnelSendSpeed,
                    e.TunnelReceiveSpeed
                });

            entity
                .HasIndex(e => new { e.ServerFarmId, e.CreatedTime })
                .IncludeProperties(e => new
                {
                    e.ServerId,
                    e.SessionCount,
                    e.TunnelSendSpeed,
                    e.TunnelReceiveSpeed
                });

            entity
                .HasIndex(e => new { e.ServerId, e.CreatedTime })
                .IncludeProperties(e => new
                {
                    e.SessionCount,
                    e.TunnelSendSpeed,
                    e.TunnelReceiveSpeed
                });
        });

        modelBuilder.Entity<AccessUsageArchive>(entity =>
        {
            entity.HasKey(e => e.AccessUsageId);

            entity
                .ToTable(nameof(AccessUsages))
                .HasKey(x => x.AccessUsageId);

            entity.Property(e => e.AccessUsageId)
                .ValueGeneratedNever();

            entity.HasIndex(e => new { e.ProjectId, e.CreatedTime })
                .IncludeProperties(e => new { e.ServerFarmId, e.ServerId, e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.ServerFarmId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.ServerId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.AccessTokenId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.ServerId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic });

            entity.HasIndex(e => new { e.DeviceId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.SentTraffic, e.ReceivedTraffic });
        });


        modelBuilder.Entity<SessionArchive>(entity =>
        {
            entity.HasKey(e => e.SessionId);

            entity.HasIndex(e => new { e.ServerId, e.CreatedTime });

            entity.Property(e => e.SessionId)
                .ValueGeneratedNever();

            entity.Property(e => e.Country)
                .HasMaxLength(10);

            entity.Property(e => e.DeviceIp)
                .HasMaxLength(50);

            entity.Property(e => e.ClientVersion)
                .HasMaxLength(20);
        });

        // ReSharper disable once InvocationIsSkipped
        OnModelCreatingPartial(modelBuilder);
    }

    // ReSharper disable once PartialMethodWithSinglePart
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}