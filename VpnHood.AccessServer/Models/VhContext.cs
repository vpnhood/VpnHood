using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class VhContext : DbContext
    {
        public VhContext()
        {
        }

        public VhContext(DbContextOptions<VhContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Account> Accounts { get; set; }
        public virtual DbSet<AccessToken> AccessTokens { get; set; }
        public virtual DbSet<AccessUsage> AccessUsages { get; set; }
        public virtual DbSet<Client> Clients { get; set; }
        public virtual DbSet<PublicCycle> PublicCycles { get; set; }
        public virtual DbSet<Server> Servers { get; set; }
        public virtual DbSet<ServerEndPoint> ServerEndPoints { get; set; }
        public virtual DbSet<ServerEndPointGroup> ServerEndPointGroups { get; set; }
        public virtual DbSet<Setting> Settings { get; set; }
        public virtual DbSet<AccessUsageLog> AccessUsageLogs { get; set; }
        public virtual DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer(App.ConnectionString);
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
                entity.HasIndex(e => new { e.SupportId })
                    .IsUnique();

                entity.Property(e => e.AccessTokenId);

                entity.Property(e => e.AccessTokenName)
                    .HasMaxLength(50);

                entity.Property(e => e.EndTime)
                    .HasColumnType("datetime");

                entity.Property(e => e.Secret)
                    .IsRequired()
                    .HasMaxLength(16)
                    .HasDefaultValueSql("(Crypt_Gen_Random((16)))")
                    .IsFixedLength(true);

                entity.Property(e => e.StartTime)
                    .HasColumnType("datetime");

                entity.Property(e => e.SupportId)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.Url)
                    .HasMaxLength(255);
            });

            modelBuilder.Entity<AccessUsage>(entity =>
            {
                entity.HasKey(e => new { e.AccessTokenId, e.ClientId });

                entity.Property(e => e.ClientId)
                    .HasMaxLength(20);

                entity.Property(e => e.ConnectTime)
                    .HasColumnType("datetime");

                entity.Property(e => e.ModifiedTime)
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<Client>(entity =>
            {
                entity.Property(e => e.ClientVersion)
                    .HasMaxLength(20);

                entity.Property(e => e.CreatedTime)
                    .HasColumnType("datetime").IsRequired();

                entity.Property(e => e.UserAgent)
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
                    .HasColumnType("datetime");

                entity.Property(e => e.Description);

                entity.Property(e => e.LastStatusTime)
                    .HasColumnType("datetime");

                entity.Property(e => e.ServerName)
                    .HasMaxLength(50);
            });

            modelBuilder.Entity<ServerEndPoint>(entity =>
            {
                entity.HasKey(e => new { e.AccountId, e.ServerEndPointId });

                entity.HasIndex(e => new { e.ServerEndPointGroupId, e.IsDefault })
                    .IsUnique()
                    .HasFilter($"{nameof(ServerEndPoint.IsDefault)} = 1");

                entity.HasIndex(e => new { e.AccountId, e.LocalEndPoint })
                    .IsUnique();

                entity.Property(e => e.ServerEndPointId)
                    .HasMaxLength(20);

                entity.Property(e => e.ServerId)
                    .IsRequired(false);

                entity.Property(e => e.CertificateRawData);

                entity.HasOne(e => e.Account)
                    .WithMany(d => d.ServerEndPoints)
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<ServerEndPointGroup>(entity =>
            {
                entity.HasIndex(e => new { e.AccountId, e.ServerEndPointGroupName })
                    .IsUnique();

                entity.Property(e => e.ServerEndPointGroupName)
                    .HasMaxLength(100);
            });

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(e => e.SettingsId);

                entity.Property(e => e.SettingsId)
                    .HasDefaultValueSql("((1))");
            });

            modelBuilder.Entity<AccessUsageLog>(entity =>
            {
                entity.Property(e => e.ClientIp)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.ClientVersion)
                    .HasMaxLength(20);

                entity.Property(e => e.CreatedTime)
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(e => e.UserId);

                entity.Property(e => e.AuthUserId)
                    .HasMaxLength(40);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
