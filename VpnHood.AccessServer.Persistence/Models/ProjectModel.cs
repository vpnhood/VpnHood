using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models.HostOrders;

namespace VpnHood.AccessServer.Persistence.Models;

public class ProjectModel
{
    public required Guid ProjectId { get; set; }
    public required string? ProjectName { get; set; }
    public required string? GaMeasurementId { get; set; }
    public required string? GaApiSecret { get; set; }
    public required string AdRewardSecret { get; set; }
    public required SubscriptionType SubscriptionType { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required LetsEncryptAccount? LetsEncryptAccount { get; set; }
    public required bool IsEnabled { get; set; }
    public required DateTime? DeletedTime { get; set; }
    public required bool HasHostProvider { get; set; }

    public virtual ICollection<ServerModel>? Servers { get; set; }
    public virtual ICollection<ServerProfileModel>? ServerProfiles { get; set; }
    public virtual ICollection<ServerFarmModel>? ServerFarms { get; set; }
    public virtual ICollection<FarmTokenRepoModel>? FarmTokenRepoModels { get; set; }
    public virtual ICollection<AccessTokenModel>? AccessTokens { get; set; }
    public virtual ICollection<DeviceModel>? Devices { get; set; }
    public virtual ICollection<ServerStatusModel>? ServerStatuses { get; set; }
    public virtual ICollection<CertificateModel>? Certificates { get; set; }
    public virtual ICollection<HostProviderModel>? ProviderConfigs { get; set; }
    public virtual ICollection<HostOrderModel>? HostOrders { get; set; }
    public virtual ICollection<HostIpModel>? HostIps { get; set; }
    public virtual ICollection<ClientFilterModel>? ClientFilters { get; set; }

}