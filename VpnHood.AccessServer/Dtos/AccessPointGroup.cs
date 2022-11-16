using System;

namespace VpnHood.AccessServer.Dtos;

public class AccessPointGroup
{
    public Guid AccessPointGroupId { get; set; }
    public string? AccessPointGroupName { get; set; }
    public Guid CertificateId { get; set; }
    public DateTime CreatedTime { get; set; }
}