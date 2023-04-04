using System;

namespace VpnHood.AccessServer.Dtos;

public class ServerProfile
{
    public required Guid ServerProfileId { get; init; }
    public required string ServerProfileName { get; init; }
    public required bool IsDefault { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required string? ServerConfig { get; init; }
}