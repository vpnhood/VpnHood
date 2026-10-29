using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.Server.Abstractions;

namespace VpnHood.Test.Providers;

public class TestNetConfigurationProvider : INetConfigurationProvider
{
    public ConcurrentDictionary<IPAddress, string> IpAddresses { get; } = new();
    public TestHold AddHold { get; } = new();

    public Task<string[]> GetInterfaceNames(CancellationToken cancellationToken)
    {
        return Task.FromResult(new[] { "eth0", "eth1" });
    }

    public async Task AddIpAddress(IPAddress ipAddress, string interfaceName, CancellationToken cancellationToken)
    {
        await AddHold.WaitIfHeld(cancellationToken);
        IpAddresses.TryAdd(ipAddress, interfaceName);
    }

    public void RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        IpAddresses.TryRemove(ipAddress, out _);
    }

    public Task<bool> IpAddressExists(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        return Task.FromResult(IpAddresses.ContainsKey(ipAddress));
    }

    public Task<string> GetTcpCongestionControl(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task SetTcpCongestionControl(string value, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}