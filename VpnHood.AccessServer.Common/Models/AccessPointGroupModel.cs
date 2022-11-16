using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models;

public class AccessPointGroupModel
{
    public Guid AccessPointGroupId { get; set; }
    public string? AccessPointGroupName { get; set; }
    public Guid ProjectId { get; set; }
    public Guid CertificateId { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual CertificateModel? Certificate { get; set; }

    [JsonIgnore] public virtual ICollection<AccessPointModel>? AccessPoints { get; set; }
    [JsonIgnore] public virtual ICollection<ServerModel>? Servers { get; set; }
    [JsonIgnore] public virtual ICollection<AccessUsageModel>? AccessUsages { get; set; }

}