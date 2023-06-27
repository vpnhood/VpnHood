using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Models;

public class ProjectModel
{
    public Guid ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GaMeasurementId { get; set; }
    public string? GaApiSecret { get; set; }
    public SubscriptionType SubscriptionType { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual ICollection<ServerModel>? Servers { get; set; }
    public virtual ICollection<ServerProfileModel>? ServerProfiles { get; set; }
    public virtual ICollection<ServerFarmModel>? ServerFarms { get; set; }
    public virtual ICollection<AccessTokenModel>? AccessTokens { get; set; }
    public virtual ICollection<DeviceModel>? Devices { get; set; }
    public virtual ICollection<ServerStatusModel>? ServerStatuses { get; set; }
}