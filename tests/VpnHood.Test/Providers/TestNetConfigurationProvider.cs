using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.Server.Abstractions;

namespace VpnHood.Test.Providers;

public class TestNetConfigurationProvider : INetConfigurationProvider
{
    public ConcurrentDictionary<IPAddress, string> IpAddresses { get; } = new();
    public TestHold AddHold { get; } = new();

    public Task<string[]> GetInterfaceNames()
    {
        return Task.FromResult(new[] { "eth0", "eth1" });
    }

    public async Task AddIpAddress(IPAddress ipAddress, string interfaceName)
    {
        await AddHold.WaitIfHeld();
        IpAddresses.TryAdd(ipAddress, interfaceName);
    }

    public void RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        IpAddresses.TryRemove(ipAddress, out _);
    }

    public Task<bool> IpAddressExists(IPAddress ipAddress)
    {
        return Task.FromResult(IpAddresses.ContainsKey(ipAddress));
    }

    public Task<string> GetTcpCongestionControl()
    {
        throw new NotSupportedException();
    }

    public Task SetTcpCongestionControl(string value)
    {
        throw new NotSupportedException();
    }
}