﻿namespace VpnHood.AccessServer.Dtos;

public class ServerInstallBySshUserKeyParams
{
    public required string HostName { get; init; }
    public required int HostPort { get; init; } = 22;
    public required string LoginUserName { get; init; }
    public string? LoginPassword { get; set; }
    public required byte[] UserPrivateKey { get; init; }
    public string? UserPrivateKeyPassphrase { get; init; }
}