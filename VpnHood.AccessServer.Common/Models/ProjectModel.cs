using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models;

public class ProjectModel
{
    public Guid ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GaTrackId { get; set; }
    public SubscriptionType SubscriptionType { get; set; }

    [JsonIgnore] 
    public virtual ICollection<ServerModel>? Servers { get; set; }

    [JsonIgnore] 
    public virtual ICollection<AccessPointGroupModel>? AccessPointGroups { get; set; }

    [JsonIgnore] 
    public virtual ICollection<AccessTokenModel>? AccessTokens { get; set; }

    [JsonIgnore] 
    public virtual ICollection<DeviceModel>? Devices { get; set; }

    [JsonIgnore] 
    public virtual ICollection<AccessUsageModel>? AccessUsages { get; set; }

    [JsonIgnore] 
    public virtual ICollection<ServerStatusModel>? ServerStatuses { get; set; }
}