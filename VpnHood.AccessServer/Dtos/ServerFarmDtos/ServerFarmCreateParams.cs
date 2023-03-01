using System;

namespace VpnHood.AccessServer.Dtos.ServerFarmDto;

public class ServerFarmCreateParams
{
    public string? ServerFarmName { get; set; }
    public Guid? CertificateId { get; set; }
}