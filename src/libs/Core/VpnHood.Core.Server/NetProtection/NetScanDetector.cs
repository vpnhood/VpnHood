using VpnHood.Net.Toolkit.Collections;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Server.NetProtection;

internal class NetScanDetector(int itemLimit, TimeSpan itemTimeout) : IDisposable
{
    private readonly TimeoutDictionary<IpAddressValue, NetworkIpAddressItem> _networkIpAddresses = new(itemTimeout);

    private static IpAddressValue GetNetworkIpAddress(IpEndPointValue ipEndPoint)
    {
        Span<byte> buffer = stackalloc byte[16];
        ipEndPoint.AddressValue.TryWriteBytes(buffer, out var length);
        buffer[3] = 0; // ipV4 only
        return new IpAddressValue(buffer[..length]);
    }

    public bool Verify(IpEndPointValue ipEndPoint)
    {
        if (ipEndPoint.IsV6)
            return true;

        var item = _networkIpAddresses.GetOrAdd(GetNetworkIpAddress(ipEndPoint),
            _ => new NetworkIpAddressItem(_networkIpAddresses.Timeout));

        item.EndPoints.GetOrAdd(ipEndPoint, _ => new TimeoutItem());

        if (item.EndPoints.Count <= itemLimit)
            return true;

        item.EndPoints.Cleanup(true);
        return item.EndPoints.Count <= itemLimit;
    }

    private class NetworkIpAddressItem(TimeSpan? timeout) : TimeoutItem
    {
        public TimeoutDictionary<IpEndPointValue, TimeoutItem> EndPoints { get; } = new(timeout);
    }

    public int GetBurstCount(IpEndPointValue ipEndPoint)
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