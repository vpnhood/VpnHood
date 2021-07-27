using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class VhContext : DbContext
    {
        private readonly bool _withSqlLog = false;

        public VhContext(bool withSqlLog = false)
        {
            _withSqlLog = withSqlLog;
        }

        public VhContext(DbContextOptions<VhContext> options, bool withSqlLog = false)
            : base(options)
        {
            _withSqlLog = withSqlLog;
        }

        public virtual DbSet<Account> Accounts { get; set; }
        public virtual DbSet<AccessToken> AccessTokens { get; set; }
        public virtual DbSet<AccessUsage> AccessUsages { get; set; }
        public virtual DbSet<Client> Clients { get; set; }
        public virtual DbSet<PublicCycle> PublicCycles { get; set; }
        public virtual DbSet<Server> Servers { get; set; }
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
                if (_withSqlLog)
                {
                    optionsBuilder.EnableSensitiveDataLogging(true);
                    optionsBuilder.LogTo(Console.WriteLine, new[] { new EventId(20101) });
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_100_CS_AS_SC_UTF8");

            modelBuilder.Entity<Account>(entity =>
            {
                entity.HasKey(e => e.AccountId);
            });


            modelBuilder.Entity<AccessToken>(entity =>
            {
                entity.HasIndex(e => new { e.SupportCode })
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
                    .HasDefaultValueSql("Crypt_Gen_Random((16))")
                    .HasMaxLength(16)
                    .IsFixedLength(true);

                entity.Property(e => e.StartTime)
                    .HasColumnType("datetime");

                entity.Property(e => e.Url)
                    .HasMaxLength(255);
            });

            modelBuilder.Entity<AccessUsage>(entity =>
            {
                entity.HasKey(e => new { e.AccessTokenId, e.ClientId });

                entity.Property(e => e.ClientId)
                    .HasMaxLength(20);

                entity.Property(e => e.ConnectTime)
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
            });

            modelBuilder.Entity<Client>(entity =>
            {
                entity.Property(e => e.ClientVersion)
                    .IsRequired(false)
                    .HasMaxLength(20);

                entity.Property(e => e.CreatedTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("getdate()");

                entity.Property(e => e.UserAgent)
                    .IsRequired(false)
                    .HasMaxLength(100);
            });

            modelBuilder.Entity<PublicCycle>(entity =>
            {
                entity.Property(e => e.PublicCycleId)
                    .HasMaxLength(12)
                    .IsFixedLength(true);
            });

            modelBuilder.Entity<Server>(entity =>
            {
                entity.HasIndex(e => new { e.AccountId, e.ServerName })
                    .IsUnique();

                entity.Property(e => e.CreatedTime)
                    .HasDefaultValueSql("getdate()")
                    .HasColumnType("datetime");

                entity.Property(e => e.LastStatusTime)
                    .HasDefaultValueSql("getdate()")
                    .HasColumnType("datetime");

                entity.Property(e => e.Description)
                    .IsRequired(false);

                entity.Property(e => e.LastSessionCount)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.ServerName)
                    .IsRequired(false)
                    .HasMaxLength(50);
            });

            modelBuilder.Entity<ServerEndPoint>(entity =>
            {
                entity.HasIndex(e => new { e.AccountId, e.PulicEndPoint })
                    .IsUnique();

                entity.HasIndex(e => new { e.AccountId, e.LocalEndPoint })
                    .IsUnique()
                    .HasFilter($"{nameof(ServerEndPoint.LocalEndPoint)} IS NOT NULL");

                entity.HasIndex(e => new { e.AccessTokenGroupId, e.IsDefault })
                    .IsUnique()
                    .HasFilter($"{nameof(ServerEndPoint.IsDefault)} = 1");

                entity.Property(e => e.PulicEndPoint)
                    .HasMaxLength(50);

                entity.Property(e => e.IsDefault)
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LocalEndPoint)
                    .HasMaxLength(50)
                    .IsRequired(false);

                entity.Property(e => e.ServerId)
                    .IsRequired(false);

                entity.Property(e => e.CertificateRawData);

                entity.HasOne(e => e.Account)
                    .WithMany(d => d.ServerEndPoints)
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<AccessTokenGroup>(entity =>
            {
                entity.HasIndex(e => new { e.AccountId, e.AccessTokenGroupName })
                    .IsUnique();

                entity.Property(e => e.AccessTokenGroupName)
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

            modelBuilder.Entity<AccessUsageLog>(entity =>
            {
                entity.HasIndex(e => e.AccessTokenId);
                entity.HasIndex(e => e.ClientId);

                entity.Property(e => e.AccessUsageLogId)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.ClientIp)
                    .IsRequired(false)
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
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(e => e.UserId)
                    .HasDefaultValueSql("newid()")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.AuthUserId)
                    .IsRequired()
                    .HasMaxLength(40);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
