using System;

namespace VpnHood.AccessServer.Dtos.ServerFarmDto;

public class ServerFarm
{
    public required Guid ServerFarmId { get; set; }
    public required string? ServerFarmName { get; set; }
    public required Guid CertificateId { get; set; }
    public required DateTime CreatedTime { get; set; }
}
