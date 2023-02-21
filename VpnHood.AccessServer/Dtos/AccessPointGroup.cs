using System;

namespace VpnHood.AccessServer.Dtos;

public class AccessPointGroup
{
    public required Guid AccessPointGroupId { get; set; }
    public required string? AccessPointGroupName { get; set; }
    public required Guid CertificateId { get; set; }
    public required DateTime CreatedTime { get; set; }
}
