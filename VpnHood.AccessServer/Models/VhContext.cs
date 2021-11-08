﻿using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Authorization.Models;
using VpnHood.Common.Logging;

#nullable disable
namespace VpnHood.AccessServer.Models
{
    // ReSharper disable once PartialTypeWithSinglePart
    public partial class VhContext : AuthDbContext
    {
        public VhContext()
        {
        }

        public VhContext(DbContextOptions<VhContext> options)
            : base(options)
        {
        }

        public bool DebugMode { get; set; } = false;

        public virtual DbSet<Project> Projects { get; set; }
        public virtual DbSet<ProjectRole> ProjectRoles { get; set; }
        public virtual DbSet<AccessToken> AccessTokens { get; set; }
        public virtual DbSet<Access> Accesses { get; set; }
        public virtual DbSet<ProjectClient> ProjectClients { get; set; }
        public virtual DbSet<PublicCycle> PublicCycles { get; set; }
        public virtual DbSet<Server> Servers { get; set; }
        public virtual DbSet<ServerStatusLog> ServerStatusLogs { get; set; }
        public virtual DbSet<AccessPoint> AccessPoints { get; set; }
        public virtual DbSet<AccessPointGroup> AccessPointGroups { get; set; }
        public virtual DbSet<Setting> Settings { get; set; }
        public virtual DbSet<Session> Sessions { get; set; }
        public virtual DbSet<AccessUsage> AccessUsages { get; set; }
        public virtual DbSet<Certificate> Certificates { get; set; }
        public virtual DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            if (optionsBuilder.IsConfigured) return;
            optionsBuilder.UseSqlServer(AccessServerApp.Instance.ConnectionString);
            if (VhLogger.IsDiagnoseMode)
            {
                optionsBuilder.EnableSensitiveDataLogging();
                optionsBuilder.LogTo(x =>
                {
                    if (DebugMode)
                        Debug.WriteLine(x);
                }, new[] {new EventId(20101)});
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_100_CS_AS_SC_UTF8");

            modelBuilder.Entity<Project>(entity => { entity.Property(e => e.ProjectId); });

            modelBuilder.Entity<Certificate>(entity =>
            {
                entity.Property(e => e.CommonName)
                    .HasMaxLength(200);
            });

            modelBuilder.Entity<AccessToken>(entity =>
            {
                entity.HasIndex(e => new {e.ProjectId, e.SupportCode})
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

            modelBuilder.Entity<ProjectClient>(entity =>
            {
                entity.HasIndex(e => new {e.ProjectId, e.ClientId})
                    .IsUnique();

                entity.HasIndex(e => e.ClientId);

                entity.Property(e => e.ClientIp)
                    .HasMaxLength(50);

                entity.Property(e => e.ClientVersion)
                    .HasMaxLength(20);

                entity.Property(e => e.UserAgent)
                    .HasMaxLength(500);

                entity.HasOne(e => e.Project)
                    .WithMany(d => d.Clients)
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
                entity.HasIndex(e => new {e.ProjectId, e.ServerName})
                    .HasFilter($"{nameof(Server.ServerName)} IS NOT NULL")
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

                entity.Property(e => e.Secret)
                    .HasMaxLength(32);
            });

            modelBuilder.Entity<ServerStatusLog>(entity =>
            {
                entity.Property(e => e.ServerStatusLogId)
                    .ValueGeneratedOnAdd();

                entity.HasIndex(e => new {e.ServerId, e.IsLast})
                    .IsUnique()
                    .HasFilter($"{nameof(ServerStatusLog.IsLast)} = 1");
            });

            modelBuilder.Entity<Session>(entity =>
            {
                entity.Property(e => e.SessionId)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.ClientIp)
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
                entity.HasIndex(e => new {e.AccessTokenId, e.ProjectClientId})
                    .IsUnique();

                entity.HasOne(e => e.ProjectClient)
                    .WithMany(d => d.Accesses)
                    .HasForeignKey(e => e.ProjectClientId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<AccessUsage>(entity =>
            {
                entity.HasIndex(e => new {e.AccessId, e.CreatedTime});

                entity.Property(e => e.AccessUsageId)
                    .ValueGeneratedOnAdd();

                entity.HasOne(e => e.Server)
                    .WithMany(d => d.AccessUsageLogs)
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(e => e.Session)
                    .WithMany(d => d.AccessLogs)
                    .HasForeignKey(e => e.SessionId)
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
}