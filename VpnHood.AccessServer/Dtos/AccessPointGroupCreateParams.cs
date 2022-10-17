using System;

namespace VpnHood.AccessServer.Dtos;

public class AccessPointGroupCreateParams
{
    public string? AccessPointGroupName { get; set; }
    public Guid? CertificateId { get; set; }
}