namespace VpnHood.AccessServer.Dtos;

public class ServerInstallBySshUserKeyParams
{
    public required string HostName { get; init; }
    public required int HostPort { get; init; } = 22;
    public required string UserName { get; init; }
    public string? UserPassword { get; set; }
    public required byte[] UserPrivateKey { get; init; }
    public string? UserPrivateKeyPassphrase { get; init; }
}