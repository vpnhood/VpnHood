using System;

namespace VpnHood.AccessServer.Dtos.ServerDtos;

public class ServerCreateParams
{
    public string? ServerName { get; init; }
    public required Guid ServerFarmId { get; init; }
    public AccessPoint[]? AccessPoints { get; init; }
}