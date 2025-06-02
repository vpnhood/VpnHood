using System.Net;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Server;

public class NetScanDetector(int itemLimit, TimeSpan itemTimeout) : IDisposable
{
    private readonly TimeoutDictionary<IPAddress, NetworkIpAddressItem> _networkIpAddresses = new(itemTimeout);

    private static IPAddress GetNetworkIpAddress(IPEndPoint ipEndPoint)
    {
        var bytes = ipEndPoint.Address.GetAddressBytesFast(stackalloc byte[16]);
        bytes[3] = 0; // ipV4 only
        return new IPAddress(bytes);
    }

    public bool Verify(IPEndPoint ipEndPoint)
    {
        if (ipEndPoint.IsV6())
            return true;

        var item = _networkIpAddresses.GetOrAdd(GetNetworkIpAddress(ipEndPoint),
            _ => new NetworkIpAddressItem(_networkIpAddresses.Timeout));

        item.EndPoints.GetOrAdd(ipEndPoint, _ => new TimeoutItem());

        if (item.EndPoints.Count <= itemLimit)
            return true;

        item.EndPoints.Cleanup(true);
        return item.EndPoints.Count < itemLimit;
    }

    private class NetworkIpAddressItem(TimeSpan? timeout) : TimeoutItem
    {
        public TimeoutDictionary<IPEndPoint, TimeoutItem> EndPoints { get; } = new(timeout);
    }

    public int GetBurstCount(IPEndPoint ipEndPoint)
    {
        return _networkIpAddresses.TryGetValue(GetNetworkIpAddress(ipEndPoint), out var value)
            ? value.EndPoints.Count
            : 0;
    }

    public void Dispose()
    {
        _networkIpAddresses.Dispose();
    }
}