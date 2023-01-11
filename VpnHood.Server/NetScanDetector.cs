using System;
using System.Net;
using System.Net.Sockets;
using VpnHood.Common.Collections;

namespace VpnHood.Server;

public class NetScanDetector
{
    private readonly int _itemLimit;
    private readonly TimeoutDictionary<IPAddress, NetworkIpAddressItem> _networkIpAddresses;

    public NetScanDetector(int itemLimit, TimeSpan itemTimeout)
    {
        _itemLimit = itemLimit;
        _networkIpAddresses = new TimeoutDictionary<IPAddress, NetworkIpAddressItem>(itemTimeout);
    }

    private IPAddress GetNetworkIpAddress(IPEndPoint ipEndPoint)
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

        if (item.EndPoints.Count <= _itemLimit)
            return true;

        item.EndPoints.Cleanup(true);
        return item.EndPoints.Count < _itemLimit;
    }

    private class NetworkIpAddressItem : TimeoutItem
    {
        public TimeoutDictionary<IPEndPoint, TimeoutItem> EndPoints { get; }

        public NetworkIpAddressItem(TimeSpan? timeout)
        {
            EndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem>(timeout);
        }
    }

    public int GetBurstCount(IPEndPoint ipEndPoint)
    {
        return _networkIpAddresses.TryGetValue(GetNetworkIpAddress(ipEndPoint), out var value)
            ? value.EndPoints.Count : 0;
    }
}