namespace VpnHood.AccessServer.Dtos.Server;

public class ServerInstallBySshUserPasswordParams
{
    public required string HostName { get; init; }
    public required int HostPort { get; init; } = 22;
    public required string LoginUserName { get; init; }
    public required string LoginPassword { get; init; }
}