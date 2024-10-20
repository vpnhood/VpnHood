using System.Net;

namespace VpnHood.AccessServer.Abstractions.Providers.Hosts;

public interface IHostProvider
{
    Task<string?> GetServerIdFromIp(IPAddress serverIp, TimeSpan timeout);
    Task<string> OrderNewIp(string serverId, TimeSpan timeout);
    Task ReleaseIp(IPAddress ipAddress, TimeSpan timeout);
    Task<IPAddress[]> ListIps(string? search, TimeSpan timeout);
    Task<HostProviderIp> GetIp(IPAddress ipAddress, TimeSpan timeout);
    Task UpdateIpDesc(IPAddress ipAddress, string? description, TimeSpan timeout);
}
