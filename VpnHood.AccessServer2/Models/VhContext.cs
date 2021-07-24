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

        public virtual DbSet<AccessToken> AccessTokens { get; set; }
        public virtual DbSet<AccessUsage> AccessUsages { get; set; }
        public virtual DbSet<Client> Clients { get; set; }
        public virtual DbSet<PublicCycle> PublicCycles { get; set; }
        public virtual DbSet<Server> Servers { get; set; }
        public virtual DbSet<ServerEndPoint> ServerEndPoints { get; set; }
        public virtual DbSet<ServerEndPointGroup> ServerEndPointGroups { get; set; }
        public virtual DbSet<Setting> Settings { get; set; }
        public virtual DbSet<UsageLog> UsageLogs { get; set; }
        public virtual DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
                optionsBuilder.UseSqlServer("Server=.;Database=Vh2;Trusted_Connection=True;");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_100_CS_AS_SC_UTF8");

            modelBuilder.Entity<AccessToken>(entity =>
            {
                entity.HasIndex(e => e.SupportId)
                    .IsUnique();

                entity.Property(e => e.AccessTokenId)
                    .HasDefaultValueSql("(newid())");

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

                entity.HasOne(d => d.ServerEndPointGroup)
                    .WithMany(p => p.AccessTokens)
                    .HasForeignKey(d => d.ServerEndPointGroupId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<AccessUsage>(entity =>
            {
                entity.HasKey(e => new { e.AccessTokenId, e.ClientIp });

                entity.Property(e => e.ClientIp)
                    .HasMaxLength(20);

                entity.HasOne(d => d.AccessToken)
                    .WithMany(p => p.AccessUsages)
                    .HasForeignKey(d => d.AccessTokenId);
            });

            modelBuilder.Entity<Client>(entity =>
            {
                entity.Property(e => e.ClientId)
                    .ValueGeneratedNever();

                entity.Property(e => e.ClientVersion)
                    .HasMaxLength(20);

                entity.Property(e => e.CreatedTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.Property(e => e.LastConnectTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

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
                entity.HasIndex(e => e.ServerName)
                    .IsUnique();

                entity.Property(e => e.ServerId)
                    .ValueGeneratedNever();

                entity.Property(e => e.CreatedTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.Property(e => e.Description)
                    .IsRequired();

                entity.Property(e => e.LastStatusTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.Property(e => e.ServerName)
                    .HasMaxLength(50);
            });

            modelBuilder.Entity<ServerEndPoint>(entity =>
            {
                entity.HasIndex(e => new { e.ServerEndPointGroupId, e.IsDefault })
                    .IsUnique()
                    .HasFilter($"{nameof(ServerEndPoint.IsDefault)} = 1");

                entity.Property(e => e.ServerEndPointId)
                    .HasMaxLength(20);

                entity.Property(e => e.CertificateRawData)
                    .IsRequired();

                entity.HasOne(d => d.ServerEndPointGroup)
                    .WithMany(p => p.ServerEndPoints)
                    .HasForeignKey(d => d.ServerEndPointGroupId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(d => d.Server)
                    .WithMany(p => p.ServerEndPoints)
                    .HasForeignKey(d => d.ServerId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<ServerEndPointGroup>(entity =>
            {
                entity.HasIndex(e => e.ServerEndPointGroupName)
                    .IsUnique();

                entity.Property(e => e.ServerEndPointGroupName)
                    .HasMaxLength(100);

                entity.Property(e => e.ServerEndPointGroupId)
                    .ValueGeneratedNever();
            });

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(e => e.SettingsId);

                entity.Property(e => e.SettingsId)
                    .HasDefaultValueSql("((1))");
            });

            modelBuilder.Entity<UsageLog>(entity =>
            {
                entity.Property(e => e.ClientIp)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.ClientVersion)
                    .HasMaxLength(20);

                entity.Property(e => e.CreatedTime)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.HasOne(d => d.Client)
                    .WithMany(p => p.UsageLogs)
                    .HasForeignKey(d => d.ClientId);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(e => e.UserId);

                entity.Property(e => e.AuthUserId)
                    .HasMaxLength(40)
                    .IsFixedLength(true);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
