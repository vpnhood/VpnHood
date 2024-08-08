using System.Net;

namespace VpnHood.AccessServer.Abstractions.Providers.Hosts;

public interface IHostProvider
{
    Task<string> GetServerIdFromIp(IPAddress serverIp);
    Task<HostProviderIpOrder> OrderNewIp(string serverId, string? description);
    Task<HostProviderIpOrder> GetOrderForNewIp(string orderId);
    Task<HostProviderIp[]> LisIps();
}
