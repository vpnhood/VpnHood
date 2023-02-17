using System;

namespace VpnHood.AccessServer.Dtos;

public class ServerProfile
{
    public required Guid ServerProfileId { get; set; }
    public string? ServerConfig { get; set; }
}