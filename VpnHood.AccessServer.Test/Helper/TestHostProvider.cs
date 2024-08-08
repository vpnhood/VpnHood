using System.Net;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;

namespace VpnHood.AccessServer.Test.Helper;

public class TestHostProvider : IHostProvider
{
    public const string ProviderName ="test_host_provider";


    public void FinishOrders()
    {
        throw new NotImplementedException();
    }

    public Task<string> GetServerIdFromIp(IPAddress serverIp)
    {
        throw new NotImplementedException();
    }

    public Task<HostProviderIpOrder> OrderNewIp(string serverId, string? description)
    {
        throw new NotImplementedException();
    }

    public Task<HostProviderIpOrder> GetOrderForNewIp(string orderId)
    {
        throw new NotImplementedException();
    }

    public Task<HostProviderIp[]> LisIps()
    {
        throw new NotImplementedException();
    }
}