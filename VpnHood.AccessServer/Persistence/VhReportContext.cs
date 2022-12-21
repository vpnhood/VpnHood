﻿#nullable disable
using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Persistence;

// ReSharper disable once PartialTypeWithSinglePart
public partial class VhReportContext : DbContext
{
    public virtual DbSet<ServerStatusModel> ServerStatuses { get; set; }
    public virtual DbSet<AccessUsageModel> AccessUsages { get; set; }
    public virtual DbSet<SessionModel> Sessions { get; set; }

    public VhReportContext(DbContextOptions<VhReportContext> options)
        : base(options)
    {
    }

    public async Task<IDbContextTransaction> WithNoLockTransaction()
    {
        Database.SetCommandTimeout(600);
        return Database.CurrentTransaction == null
            ? await Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted)
            : null;
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

        modelBuilder.Entity<ServerStatusModel>(entity =>
        {
            entity.HasKey(e => e.ServerStatusId);

            entity
                .ToTable(nameof(ServerStatuses))
                .HasKey(e => e.ServerStatusId);

            entity.Property(e => e.ServerStatusId)
                .ValueGeneratedNever();

            entity
                .HasIndex(e => new { e.ProjectId, e.CreatedTime })
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

            entity.Ignore(x => x.Project);
            entity.Ignore(x => x.Server);
            entity.Ignore(x => x.IsLast);
        });

        modelBuilder.Entity<AccessUsageModel>(entity =>
        {
            entity.HasKey(e => e.AccessUsageId);

            entity
                .ToTable(nameof(AccessUsages))
                .HasKey(x => x.AccessUsageId);

            entity.Property(e => e.AccessUsageId)
                .ValueGeneratedNever();

            entity.HasIndex(e => new { e.ProjectId, e.CreatedTime })
                .IncludeProperties(e => new { e.SessionId, e.DeviceId, e.SentTraffic, e.ReceivedTraffic, e.AccessTokenId, e.AccessPointGroupId });

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


        modelBuilder.Entity<SessionModel>(entity =>
        {
            entity.HasKey(e => e.SessionId);

            entity.Property(e => e.SessionId)
                .ValueGeneratedNever();

            entity.Property(e => e.Country)
                .HasMaxLength(10);

            entity.Property(e => e.DeviceIp)
                .HasMaxLength(50);

            entity.Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity.Ignore(e => e.Server);
            entity.Ignore(e => e.Device);
            entity.Ignore(e => e.Access);
            entity.Ignore(e => e.AccessUsages);
            entity.Ignore(e => e.IsArchived);
        });

        // ReSharper disable once InvocationIsSkipped
        OnModelCreatingPartial(modelBuilder);
    }

    // ReSharper disable once PartialMethodWithSinglePart
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}