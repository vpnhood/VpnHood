using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.Server.Abstractions;

namespace VpnHood.Test.Providers;

public class TestNetConfigurationProvider : INetConfigurationProvider
{
    public ConcurrentDictionary<IPAddress, string> IpAddresses { get; } = new();

    public Task<string[]> GetInterfaceNames()
    {
        return Task.FromResult(new[] { "eth0", "eth1" });
    }

    public Task AddIpAddress(IPAddress ipAddress, string interfaceName)
    {
        IpAddresses.TryAdd(ipAddress, interfaceName);
        return Task.CompletedTask;
    }

    public Task RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        IpAddresses.TryRemove(ipAddress, out _);
        return Task.CompletedTask;
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