using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models;

public class Project
{
    public Guid ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GaTrackId { get; set; }
    public SubscriptionType SubscriptionType { get; set; }

    [JsonIgnore] 
    public virtual ICollection<ServerModel>? Servers { get; set; }

    [JsonIgnore] 
    public virtual ICollection<AccessPointGroup>? AccessPointGroups { get; set; }

    [JsonIgnore] 
    public virtual ICollection<AccessTokenModel>? AccessTokens { get; set; }

    [JsonIgnore] 
    public virtual ICollection<Device>? Devices { get; set; }

    [JsonIgnore] 
    public virtual ICollection<AccessUsageEx>? AccessUsages { get; set; }

    [JsonIgnore] 
    public virtual ICollection<ServerStatusEx>? ServerStatuses { get; set; }
}