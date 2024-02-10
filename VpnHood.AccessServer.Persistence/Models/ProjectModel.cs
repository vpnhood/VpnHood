using VpnHood.AccessServer.Persistence.Enums;

namespace VpnHood.AccessServer.Persistence.Models;

public class ProjectModel
{
    public Guid ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GaMeasurementId { get; set; }
    public string? GaApiSecret { get; set; }
    public int CsrCount { get; set; }
    public SubscriptionType SubscriptionType { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual ICollection<ServerModel>? Servers { get; set; }
    public virtual ICollection<ServerProfileModel>? ServerProfiles { get; set; }
    public virtual ICollection<ServerFarmModel>? ServerFarms { get; set; }
    public virtual ICollection<AccessTokenModel>? AccessTokens { get; set; }
    public virtual ICollection<DeviceModel>? Devices { get; set; }
    public virtual ICollection<ServerStatusModel>? ServerStatuses { get; set; }
}

public class ProjectExModel //todo
{
    public int Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string AcmeAccountPem { get; set; }
}
