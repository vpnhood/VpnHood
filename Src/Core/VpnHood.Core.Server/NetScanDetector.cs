using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Collections;

namespace VpnHood.Core.Server;

public class NetScanDetector(int itemLimit, TimeSpan itemTimeout)
{
    private readonly TimeoutDictionary<IPAddress, NetworkIpAddressItem> _networkIpAddresses = new(itemTimeout);

    private static IPAddress GetNetworkIpAddress(IPEndPoint ipEndPoint)
    {
        var bytes = ipEndPoint.Address.GetAddressBytes();
        bytes[3] = 0;
        return new IPAddress(bytes);
    }

    public bool Verify(IPEndPoint ipEndPoint)
    {
        if (ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
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
}