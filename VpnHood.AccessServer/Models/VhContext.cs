using System;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using VpnHood.Logging;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class VhContext : DbContext
    {
        public bool DebugMode { get; set; } = false;

        public VhContext()
        {
        }

        public VhContext(DbContextOptions<VhContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Project> Projects { get; set; }
        public virtual DbSet<AccessToken> AccessTokens { get; set; }
        public virtual DbSet<AccessUsage> AccessUsages { get; set; }
        public virtual DbSet<Client> Clients { get; set; }
        public virtual DbSet<PublicCycle> PublicCycles { get; set; }
        public virtual DbSet<Server> Servers { get; set; }
        public virtual DbSet<ServerStatusLog> ServerStatusLogs { get; set; }
        public virtual DbSet<ServerEndPoint> ServerEndPoints { get; set; }
        public virtual DbSet<AccessTokenGroup> AccessTokenGroups { get; set; }
        public virtual DbSet<Setting> Settings { get; set; }
        public virtual DbSet<AccessUsageLog> AccessUsageLogs { get; set; }
        public virtual DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer(App.ConnectionString);
                if (VhLogger.IsDiagnoseMode)
                {
                    optionsBuilder.EnableSensitiveDataLogging(true);
                    optionsBuilder.LogTo((x) =>
                    {
                        if (DebugMode)
                            Debug.WriteLine(x);
                    }, new[] { new EventId(20101) });
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_100_CS_AS_SC_UTF8");

            modelBuilder.Entity<Project>(entity =>
            {
                entity.Property(e => e.ProjectId)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("newid()");
            });

            modelBuilder.Entity<AccessToken>(entity =>
            {
                entity.HasIndex(e => new { e.ProjectId, e.SupportCode })
                    .IsUnique();

                entity.Property(e => e.AccessTokenId)
                    .HasDefaultValueSql("newid()")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.AccessTokenName)
                    .HasMaxLength(50);

                entity.Property(e => e.Lifetime)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.MaxTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.MaxClient)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.IsPublic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.EndTime)
                    .HasColumnType("datetime");

                entity.Property(e => e.Secret)
                    .IsRequired()
                    .HasDefaultValueSql("Crypt_Gen_Random((16))")
                    .HasMaxLength(16)
                    .IsFixedLength(true);

                entity.Property(e => e.StartTime)
                    .HasColumnType("datetime");

                entity.Property(e => e.Url)
                    .IsRequired(false)
                    .HasMaxLength(255);

                entity.HasOne(e => e.Project)
                    .WithMany(d => d.AccessTokens)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<Client>(entity =>
            {
                entity.HasKey(e => e.ClientKeyId);
                
                entity.Property(e=>e.ClientKeyId)
                    .ValueGeneratedOnAdd();

                entity.HasIndex(e => new { e.ProjectId, e.ClientId })
                    .IsUnique();

                entity.HasIndex(e => e.ClientId);

                entity.Property(e => e.ClientIp)
                    .IsRequired(false)
                    .IsUnicode(false)
                    .HasMaxLength(50);

                entity.Property(e => e.ClientVersion)
                    .IsRequired(false)
                    .IsUnicode(false)
                    .HasMaxLength(20);

                entity.Property(e => e.CreatedTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("getdate()");

                entity.Property(e => e.UserAgent)
                    .IsRequired(false)
                    .HasMaxLength(100);

                entity.HasOne(e => e.Project)
                    .WithMany(d => d.Clients)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<PublicCycle>(entity =>
            {
                entity.Property(e => e.PublicCycleId)
                    .HasMaxLength(12)
                    .IsFixedLength(true);
            });

            modelBuilder.Entity<Server>(entity =>
            {
                entity.Property(e => e.ServerId)
                    .HasDefaultValueSql("newid()")
                    .ValueGeneratedOnAdd();

                entity.HasIndex(e => new { e.ProjectId, e.ServerName })
                    .HasFilter($"{nameof(Server.ServerName)} IS NOT NULL")
                    .IsUnique();

                entity.Property(e => e.CreatedTime)
                    .HasDefaultValueSql("getdate()")
                    .HasColumnType("datetime");

                entity.Property(e => e.SubscribeTime)
                    .HasDefaultValueSql("getdate()")
                    .HasColumnType("datetime");

                entity.Property(e => e.Description)
                    .IsRequired(false)
                    .HasMaxLength(400);

                entity.Property(e => e.ServerName)
                    .IsRequired(false)
                    .HasMaxLength(100);

                entity.Property(e => e.OsInfo)
                    .IsRequired(false)
                    .IsUnicode(false)
                    .HasMaxLength(200);

                entity.Property(e => e.Version)
                    .IsRequired(false)
                    .HasMaxLength(100);

                entity.Property(e => e.EnvironmentVersion)
                    .IsRequired(false)
                    .IsUnicode(false)
                    .HasMaxLength(100);

                entity.Property(e => e.MachineName)
                    .IsRequired(false)
                    .HasMaxLength(100);
            });

            modelBuilder.Entity<ServerStatusLog>(entity =>
            {
                entity.HasIndex(e => new { e.ServerId, e.IsLast })
                    .IsUnique()
                    .HasFilter($"{nameof(Models.ServerStatusLog.IsLast)} = 1");

                entity.Property(e => e.ServerStatusLogId)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.CreatedTime)
                    .HasDefaultValueSql("getdate()")
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<ServerEndPoint>(entity =>
            {
                entity.HasIndex(e => new { e.ProjectId, e.PulicEndPoint })
                    .IsUnique();

                entity.HasIndex(e => new { e.ProjectId, e.PrivateEndPoint })
                    .IsUnique()
                    .HasFilter($"{nameof(ServerEndPoint.PrivateEndPoint)} IS NOT NULL");

                entity.HasIndex(e => new { e.AccessTokenGroupId, e.IsDefault })
                    .IsUnique()
                    .HasFilter($"{nameof(ServerEndPoint.IsDefault)} = 1");

                entity.Property(e => e.PulicEndPoint)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.IsDefault)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.PrivateEndPoint)
                    .HasMaxLength(50)
                    .IsRequired(false);

                entity.Property(e => e.ServerId)
                    .IsRequired(false);

                entity.Property(e => e.CertificateRawData)
                    .IsRequired();

                entity.Property(e => e.CertificateCommonName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.HasOne(e => e.Project)
                    .WithMany(d => d.ServerEndPoints)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<AccessTokenGroup>(entity =>
            {
                entity.HasIndex(e => new { e.ProjectId, e.AccessTokenGroupName })
                    .IsUnique();

                entity.HasIndex(e => new { e.ProjectId, e.IsDefault })
                .IsUnique()
                .HasFilter($"{nameof(ServerEndPoint.IsDefault)} = 1");

                entity.Property(e => e.AccessTokenGroupName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.IsDefault)
                    .HasDefaultValueSql("0");
            });

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.Property(e => e.SettingId)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("((1))");
            });

            modelBuilder.Entity<AccessUsage>(entity =>
            {
                entity.Property(e => e.AccessUsageId)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("newid()");

                entity.HasIndex(e => new { e.AccessTokenId, e.ClientKeyId })
                    .IsUnique();

                entity.Property(e => e.ClientKeyId)
                    .IsRequired(false)
                    .HasMaxLength(20);

                entity.Property(e => e.ConnectTime)
                    .IsRequired()
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("getdate()");

                entity.Property(e => e.ModifiedTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("getdate()");

                entity.Property(e => e.CycleReceivedTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.CycleSentTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TotalReceivedTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TotalSentTraffic)
                    .HasDefaultValueSql("0");

                entity.HasOne(e => e.Client)
                  .WithMany(d => d.AccessUsages)
                  .HasForeignKey(e => e.ClientKeyId);
            });

            modelBuilder.Entity<AccessUsageLog>(entity =>
            {
                entity.HasIndex(e => e.AccessUsageId);

                entity.Property(e => e.AccessUsageLogId)
                    .ValueGeneratedOnAdd();

                entity.HasIndex(e => e.ClientKeyId);

                entity.Property(e => e.ClientKeyId)
                    .IsRequired();

                entity.Property(e => e.ClientIp)
                    .IsRequired(false)
                    .IsUnicode(false)
                    .HasMaxLength(50);

                entity.Property(e => e.ClientVersion)
                    .IsRequired(false)
                    .HasMaxLength(20);

                entity.Property(e => e.ReceivedTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.SentTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.CycleReceivedTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.CycleSentTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TotalReceivedTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TotalSentTraffic)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.CreatedTime)
                    .HasDefaultValueSql("getdate()")
                    .HasColumnType("datetime");

                entity.HasOne(e => e.Server)
                    .WithMany(d => d.AccessUsageLogs)
                    .HasForeignKey(e => e.ServerId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(e => e.Client)
                  .WithMany(d => d.AccessUsageLogs)
                  .HasForeignKey(e => e.ClientKeyId);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(e => e.UserId)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("newid()");

                entity.Property(e => e.AuthUserId)
                    .IsRequired()
                    .HasMaxLength(40);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
