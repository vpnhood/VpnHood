using System.Net;

namespace VpnHood.Core.Server.Abstractions;

public interface INetConfigurationProvider
{
    Task<string[]> GetInterfaceNames(CancellationToken cancellationToken);
    Task AddIpAddress(IPAddress ipAddress, string interfaceName, CancellationToken cancellationToken);
    // sync, as the server's quick close runs it too: a local command of milliseconds
    void RemoveIpAddress(IPAddress ipAddress, string interfaceName);
    Task<bool> IpAddressExists(IPAddress ipAddress, CancellationToken cancellationToken);
    Task<string> GetTcpCongestionControl(CancellationToken cancellationToken);
    Task SetTcpCongestionControl(string value, CancellationToken cancellationToken);
}