using System;

namespace VpnHood.AccessServer.Dtos;

public class ServerCreateParams
{
    public string? ServerName { get; init; }
    public required Guid AccessPointGroupId { get; init; }
    public AccessPoint[]? AccessPoints { get; init; }
}