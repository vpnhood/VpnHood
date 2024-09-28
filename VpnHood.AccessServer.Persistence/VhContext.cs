using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Models.HostOrders;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Persistence;

public class VhContext : DbContext
{
    public const int MaxDescriptionLength = 1000;
    public virtual DbSet<ProjectModel> Projects { get; set; } = default!;
    public virtual DbSet<AccessTokenModel> AccessTokens { get; set; } = default!;
    public virtual DbSet<AccessModel> Accesses { get; set; } = default!;
    public virtual DbSet<DeviceModel> Devices { get; set; } = default!;
    public virtual DbSet<PublicCycleModel> PublicCycles { get; set; } = default!;
    public virtual DbSet<ServerModel> Servers { get; set; } = default!;
    public virtual DbSet<ServerStatusModel> ServerStatuses { get; set; } = default!;
    public virtual DbSet<ServerFarmModel> ServerFarms { get; set; } = default!;
    public virtual DbSet<FarmTokenRepoModel> FarmTokenRepos { get; set; } = default!;
    public virtual DbSet<SessionModel> Sessions { get; set; } = default!;
    public virtual DbSet<AccessUsageModel> AccessUsages { get; set; } = default!;
    public virtual DbSet<CertificateModel> Certificates { get; set; } = default!;
    public virtual DbSet<IpLockModel> IpLocks { get; set; } = default!;
    public virtual DbSet<ServerProfileModel> ServerProfiles { get; set; } = default!;
    public virtual DbSet<HostProviderModel> HostProviders { get; set; } = default!;
    public virtual DbSet<LocationModel> Locations { get; set; } = default!;
    public virtual DbSet<HostOrderModel> HostOrders { get; set; } = default!;
    public virtual DbSet<HostIpModel> HostIps { get; set; } = default!;
    public virtual DbSet<ClientFilterModel> ClientFilters { get; set; }

    protected VhContext()
    {
    }

    public VhContext(DbContextOptions options)
        : base(options)
    {
    }

    public async Task<IDbContextTransaction?> WithNoLockTransaction()
    {
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
            .HaveMaxLength(450);

        configurationBuilder.Properties<byte>()
            .HaveMaxLength(450);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_100_CI_AS_SC_UTF8");

        modelBuilder.Entity<ProjectModel>(entity => {
            entity
                .HasKey(e => e.ProjectId);

            entity
                .Property(e => e.GaMeasurementId)
                .HasMaxLength(50);

            entity
                .Property(e => e.GaApiSecret)
                .HasMaxLength(50);

            entity
                .Property(e => e.AdRewardSecret)
                .HasMaxLength(50);

            entity
                .Property(e => e.ProjectName)
                .HasMaxLength(200);

            entity
                .Property(e => e.HasHostProvider)
                .HasDefaultValue(false);

            entity
                .HasOne(e => e.LetsEncryptAccount)
                .WithOne(d => d.Project)
                .HasForeignKey<LetsEncryptAccount>(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IpLockModel>(entity => {
            entity
                .HasKey(e => new { e.ProjectId, e.IpAddress });

            entity
                .Property(e => e.IpAddress)
                .HasMaxLength(40);

            entity
                .Property(e => e.Description)
                .HasMaxLength(MaxDescriptionLength);
        });

        modelBuilder.Entity<CertificateModel>(entity => {
            entity
                .HasKey(e => e.CertificateId);

            entity
                .Property(e => e.IsValidated)
                .HasDefaultValue(false);

            entity
                .Property(e => e.CommonName)
                .HasMaxLength(200);

            entity
                .Property(e => e.Thumbprint)
                .HasMaxLength(200);

            // Add milliseconds to the datetime (Required for sorting)
            entity
                .Property(e => e.CreatedTime)
                .HasPrecision(3);

            entity
                .HasIndex(e => new { e.ServerFarmId, IsDefault = e.IsInToken })
                .HasFilter($"{nameof(CertificateModel.IsDeleted)} = 0 and {nameof(CertificateModel.IsInToken)} = 1")
                .IsUnique();

            entity
                .HasIndex(e => new { e.AutoValidate, e.ExpirationTime })
                .IncludeProperties(e => new { e.ValidateErrorCount })
                .HasFilter($"{nameof(CertificateModel.IsDeleted)} = 0 and {nameof(CertificateModel.AutoValidate)} = 1");

            entity
                .HasIndex(x => new { x.ServerFarmId, x.CommonName })
                .HasFilter($"{nameof(CertificateModel.IsDeleted)} = 0");

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.Certificates)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.ServerFarm)
                .WithMany(d => d.Certificates)
                .HasForeignKey(e => e.ServerFarmId)
                .OnDelete(DeleteBehavior.Cascade);

            // make sure the certificate farm belong to the same project
        });

        modelBuilder.Entity<AccessTokenModel>(entity => {
            entity
                .HasKey(e => e.AccessTokenId);

            entity
                .HasIndex(e => new { e.ProjectId, e.SupportCode })
                .IsUnique();

            entity.Property(e => e.AccessTokenName)
                .HasMaxLength(50);

            entity
                .Property(e => e.Secret)
                .HasMaxLength(16)
                .IsFixedLength();

            entity
                .Property(e => e.Description)
                .HasMaxLength(200);

            entity
                .Property(e => e.AdRequirement)
                .HasDefaultValue(AdRequirement.None)
                .HasMaxLength(50);

            entity
                .Property(e => e.Tags)
                .HasMaxLength(40);

            entity
                .Property(e => e.IsDeleted)
                .HasDefaultValue(false);

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.AccessTokens)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.ServerFarm)
                .WithMany(d => d.AccessTokens)
                .HasForeignKey(e => e.ServerFarmId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceModel>(entity => {
            entity
                .HasKey(e => e.DeviceId);

            entity
                .HasIndex(e => new { e.ProjectId, e.ClientId })
                .IsUnique();

            entity
                .HasIndex(e => new { e.ProjectId, e.CreatedTime });

            entity
                .HasIndex(e => new { e.ProjectId, e.ModifiedTime });

            entity
                .Property(e => e.IpAddress)
                .HasMaxLength(50);

            entity
                .Property(e => e.Country)
                .HasMaxLength(10);

            entity
                .Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity
                .Property(e => e.UserAgent)
                .HasMaxLength(500);

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.Devices)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PublicCycleModel>(entity => {
            entity
                .HasKey(e => e.PublicCycleId);

            entity
                .Property(e => e.PublicCycleId)
                .HasMaxLength(12)
                .IsFixedLength();
        });

        modelBuilder.Entity<ServerModel>(entity => {
            entity.HasKey(e => e.ServerId);

            entity
                .HasIndex(e => new { e.ProjectId, e.ServerName })
                .HasFilter($"{nameof(ServerModel.IsDeleted)} = 0")
                .IsUnique();

            entity
                .Property(e => e.LastConfigError)
                .HasMaxLength(2000);

            entity
                .Property(e => e.ServerName)
                .HasMaxLength(100);

            entity
                .Property(e => e.OsInfo)
                .HasMaxLength(500);

            entity
                .Property(e => e.Version)
                .HasMaxLength(100);

            entity
                .Property(e => e.EnvironmentVersion)
                .HasMaxLength(100);

            entity
                .Property(e => e.MachineName)
                .HasMaxLength(100);

            entity
                .Property(e => e.LastConfigError)
                .HasMaxLength(2000);

            entity
                .Property(e => e.ManagementSecret)
                .HasMaxLength(16)
                .IsFixedLength();

            entity
                .Property(e => e.AllowInAutoLocation)
                .HasDefaultValue(true);

            entity
                .Property(e => e.IsDeleted)
                .HasDefaultValue(false);

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.Servers)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.ServerFarm)
                .WithMany(d => d.Servers)
                .HasForeignKey(e => e.ServerFarmId)
                .HasPrincipalKey(e => e.ServerFarmId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .OwnsMany(e => e.AccessPoints, ap => {
                    ap.ToTable(nameof(ServerModel.AccessPoints));
                    ap.WithOwner().HasForeignKey(nameof(ServerModel.ServerId));
                });

            entity
                .HasOne(e => e.Location)
                .WithMany(d => d.Servers)
                .HasForeignKey(e => e.LocationId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.ClientFilter)
                .WithMany(d => d.Servers)
                .HasPrincipalKey(e => new { e.ProjectId, e.ClientFilterId })
                .HasForeignKey(d => new { d.ProjectId, d.ClientFilterId })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ServerStatusModel>(entity => {
            entity
                .HasKey(e => e.ServerStatusId);

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
                .IncludeProperties(e => new {
                    e.SessionCount,
                    e.TcpConnectionCount,
                    e.UdpConnectionCount,
                    e.AvailableMemory,
                    e.CpuUsage,
                    e.ThreadCount,
                    e.TunnelSendSpeed,
                    e.TunnelReceiveSpeed,
                    e.IsConfigure,
                    e.CreatedTime
                })
                .IsUnique()
                .HasFilter($"{nameof(ServerStatusModel.IsLast)} = 1");

            entity
                .HasIndex(e => new { e.CreatedTime })
                .HasFilter($"{nameof(ServerStatusModel.IsLast)} = 1");

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.ServerStatuses)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ServerFarmModel>(entity => {
            entity
                .HasKey(e => e.ServerFarmId);

            entity
                .HasIndex(e => new { e.ProjectId, e.ServerFarmName })
                .HasFilter($"{nameof(ServerFarmModel.IsDeleted)} = 0")
                .IsUnique();

            entity
                .Property(e => e.PushTokenToClient)
                .HasDefaultValue(true);

            entity
                .Property(e => e.ServerFarmName)
                .HasMaxLength(100);

            entity
                .Property(e => e.UseHostName)
                .HasDefaultValue(false);

            entity
                .Property(e => e.TokenJson)
                .HasMaxLength(4000);

            entity
                .Property(e => e.TokenIv)
                .HasMaxLength(32);

            entity
                .Property(e => e.IsDeleted)
                .HasDefaultValue(false);

            entity
                .Property(e => e.Secret)
                .HasMaxLength(16)
                .IsFixedLength();

            entity.Property(e => e.MaxCertificateCount)
                .HasDefaultValue(1);

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.ServerFarms)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(e => e.ServerProfile)
                .WithMany(d => d.ServerFarms)
                .HasForeignKey(e => new { e.ProjectId, e.ServerProfileId })
                .HasPrincipalKey(e => new { e.ProjectId, e.ServerProfileId })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FarmTokenRepoModel>(entity => {
            entity
                .HasKey(e => e.FarmTokenRepoId);

            entity
                .HasIndex(e => e.IsPendingUpload)
                .HasFilter($"{nameof(FarmTokenRepoModel.IsPendingUpload)} = 1")
                .IsUnique(false);

            entity
                .Property(e => e.RepoSettings)
                .HasMaxLength(1500);

            entity
                .HasOne(e => e.ServerFarm)
                .WithMany(d => d.TokenRepos)
                .HasPrincipalKey(d => new { d.ProjectId, d.ServerFarmId })
                .HasForeignKey(e => new { e.ProjectId, e.ServerFarmId })
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.FarmTokenRepoModels)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AccessModel>(entity => {
            entity
                .HasKey(e => e.AccessId);

            entity
                .HasIndex(e => new { e.AccessTokenId, e.DeviceId })
                .IsUnique()
                .HasFilter(null); //required to prevent EF created filtered index

            entity
                .Property(e => e.Description)
                .HasMaxLength(MaxDescriptionLength);

            entity
                .Property(e => e.LastCycleTraffic)
                .HasComputedColumnSql(
                    $"{nameof(AccessModel.LastCycleSentTraffic)} + {nameof(AccessModel.LastCycleReceivedTraffic)} - {nameof(AccessModel.LastCycleSentTraffic)} - {nameof(AccessModel.LastCycleReceivedTraffic)}");

            entity
                .HasIndex(e => new { e.CycleTraffic }); // for resetting cycles

            entity
                .Property(e => e.CycleTraffic)
                .HasComputedColumnSql(
                    $"{nameof(AccessModel.TotalSentTraffic)} + {nameof(AccessModel.TotalReceivedTraffic)} - {nameof(AccessModel.LastCycleSentTraffic)} - {nameof(AccessModel.LastCycleReceivedTraffic)}");

            entity
                .Property(e => e.TotalTraffic)
                .HasComputedColumnSql(
                    $"{nameof(AccessModel.TotalSentTraffic)} + {nameof(AccessModel.TotalReceivedTraffic)}");

            entity.HasOne(e => e.Device)
                .WithMany(d => d.Accesses)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.AccessToken)
                .WithMany(d => d.Accesses)
                .HasForeignKey(e => e.AccessTokenId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionModel>(entity => {
            entity
                .HasKey(e => e.SessionId);

            //index for finding other active sessions of an AccessId
            entity
                .HasIndex(e => e.AccessId)
                .HasFilter($"{nameof(SessionModel.EndTime)} IS NULL");

            entity
                .HasIndex(e => new { e.EndTime }); //for sync 

            entity
                .Property(e => e.IsArchived);

            entity
                .Property(e => e.SessionId)
                .ValueGeneratedOnAdd();

            entity
                .Property(e => e.DeviceIp)
                .HasMaxLength(50);

            entity
                .Property(e => e.Country)
                .HasMaxLength(10);

            entity
                .Property(e => e.ClientVersion)
                .HasMaxLength(20);

            entity
                .Property(e => e.SessionKey)
                .HasMaxLength(16)
                .IsFixedLength();

            entity
                .Property(e => e.ExtraData)
                .HasMaxLength(100);

            entity.Property(e => e.ErrorMessage)
                .HasMaxLength(1000);

            entity.HasOne(e => e.Server)
                .WithMany(d => d.Sessions)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Device)
                .WithMany(d => d.Sessions)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Access)
                .WithMany(d => d.Sessions)
                .HasForeignKey(e => e.AccessId)
                .OnDelete(DeleteBehavior.Cascade);

        });


        modelBuilder.Entity<AccessUsageModel>(entity => {
            entity
                .HasKey(x => x.AccessUsageId);

            entity
                .Property(e => e.AccessUsageId)
                .ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<ServerProfileModel>(entity => {
            entity
                .HasKey(x => x.ServerProfileId);

            entity
                .HasIndex(e => new { e.ProjectId, e.ServerProfileName })
                .IsUnique();

            entity
                .HasIndex(e => new { e.ProjectId, e.IsDefault })
                .HasFilter($"{nameof(ServerProfileModel.IsDefault)} = 1")
                .IsUnique();

            entity
                .Property(x => x.ServerProfileName)
                .HasMaxLength(200);

            entity
                .Property(x => x.ServerConfig)
                .HasMaxLength(4000);

            entity
                .Property(x => x.IsDefault)
                .HasDefaultValue(false);

            entity
                .Property(x => x.IsDeleted)
                .HasDefaultValue(false);

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.ServerProfiles)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LocationModel>(entity => {
            entity
                .HasKey(x => x.LocationId);

            entity
                .HasIndex(x => new { x.CountryCode, x.RegionCode, x.CityCode })
                .IsUnique();

            entity
                .Property(x => x.RegionName)
                .HasMaxLength(50);
        });

        modelBuilder.Entity<HostProviderModel>(entity => {
            entity
                .HasKey(e => e.HostProviderId);

            entity
                .Property(e => e.CustomData)
                .HasMaxLength(int.MaxValue);

            entity
                .HasIndex(e => new { e.ProjectId, e.HostProviderName })
                .IsUnique();
        });

        modelBuilder.Entity<HostIpModel>(entity => {
            entity
                .HasKey(e => e.HostIpId);

            entity
                .HasIndex(e => new { e.ProjectId, e.CreatedTime })
                .HasFilter($"{nameof(HostIpModel.DeletedTime)} is null");

            entity
                .HasIndex(e => new { e.ProjectId, e.IpAddress })
                .HasFilter($"{nameof(HostIpModel.DeletedTime)} is null")
                .IsUnique();

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.HostIps)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(e => e.HostProvider)
                .WithMany(d => d.HostIps)
                .HasForeignKey(d => d.HostProviderId)
                .OnDelete(DeleteBehavior.NoAction);

        });

        modelBuilder.Entity<HostOrderModel>(entity => {
            entity
                .HasKey(e => e.HostOrderId);

            entity
                .Property(e => e.NewIpOrderIpAddress)
                .HasMaxLength(50);

            entity
                .HasIndex(e => new { e.ProjectId, e.Status });

            entity
                .HasIndex(e => new { e.ProjectId, e.Status })
                .HasFilter($"{nameof(HostOrderModel.Status)} = {(int)HostOrderStatus.Pending}");

            entity
                .HasIndex(e => new { e.ProjectId, e.CreatedTime })
                .IsDescending();

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.HostOrders)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(e => e.NewIpOrderServer)
                .WithMany(d => d.HostOrders)
                .HasForeignKey(d => d.NewIpOrderServerId)
                .OnDelete(DeleteBehavior.NoAction);

            entity
                .HasOne(e => e.HostProvider)
                .WithMany(d => d.HostOrders)
                .HasForeignKey(d => d.HostProviderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ClientFilterModel>(entity => {
            entity
                .HasKey(e => e.ClientFilterId);

            entity
                .HasIndex(e => new { e.ProjectId, e.ClientFilterName })
                .IsUnique();

            entity
                .HasOne(e => e.Project)
                .WithMany(d => d.ClientFilterModels)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

        });
    }
}