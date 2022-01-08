using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models;

public class AccessPointGroup
{
    public Guid AccessPointGroupId { get; set; }
    public string? AccessPointGroupName { get; set; }
    public Guid ProjectId { get; set; }
    public Guid CertificateId { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual Project? Project { get; set; }
    public virtual Certificate? Certificate { get; set; }

    [JsonIgnore] public virtual ICollection<AccessPoint>? AccessPoints { get; set; }
    [JsonIgnore] public virtual ICollection<Server>? Servers { get; set; }
    [JsonIgnore] public virtual ICollection<AccessUsageEx>? AccessUsages { get; set; }

}