﻿using System;

namespace VpnHood.AccessServer.Dtos.ServerFarmDtos;

public class ServerFarm
{
    public required Guid ServerFarmId { get; init; }
    public required string ServerFarmName { get; init; }
    public required Guid ServerProfileId { get; init; }
    public required string? ServerProfileName { get; init; }
    public required Guid CertificateId { get; init; }
    public required DateTime CreatedTime { get; init; }
}
