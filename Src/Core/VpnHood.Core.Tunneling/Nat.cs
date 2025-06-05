using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

public class Nat(bool isDestinationSensitive) : IDisposable
{
    private readonly object _lockObject = new();
    private readonly Dictionary<(IpVersion, IpProtocol), ushort> _lastNatIds = new();
    private readonly Dictionary<(IpVersion, IpProtocol, ushort), NatItem> _map = new();
    private readonly Dictionary<NatItem, NatItem> _mapR = new();
    private bool _disposed;
    private DateTime _lastCleanupTime = FastDateTime.Now;

    public TimeSpan TcpTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int ItemCount {
        get {
            lock (_lockObject)
                return _map.Count;
        }
    }

    public int GetItemCount(IpProtocol protocol)
    {
        lock (_lockObject)
            return _map.Count(x => x.Value.Protocol == protocol);
    }


    private NatItem CreateNatItemFromPacket(IpPacket ipPacket)
    {
        return isDestinationSensitive ? new NatItemEx(ipPacket) : new NatItem(ipPacket);
    }

    private bool IsExpired(NatItem natItem)
    {
        if (natItem.Protocol == IpProtocol.Tcp)
            return FastDateTime.Now - natItem.AccessTime > TcpTimeout;
        if (natItem.Protocol is IpProtocol.IcmpV4 or IpProtocol.IcmpV6)
            return FastDateTime.Now - natItem.AccessTime > IcmpTimeout;

        //treat other as UDP
        return FastDateTime.Now - natItem.AccessTime > UdpTimeout;
    }

    public void Cleanup()
    {
        if (FastDateTime.Now - _lastCleanupTime < IcmpTimeout)
            return;
        _lastCleanupTime = FastDateTime.Now;

        // select the expired items
        NatItem[] items;
        lock (_lockObject)
            items = _mapR.Values.Where(IsExpired).ToArray();

        foreach (var item in items)
            Remove(item);
    }

    private void Remove(NatItem natItem)
    {
        NatItem? natItem2;
        lock (_lockObject) {
            _mapR.Remove(natItem, out natItem2);
            _map.Remove((natItem.IpVersion, natItem.Protocol, natItem.NatId), out _);
        }

        if (natItem2 != null && VhLogger.MinLogLevel <= LogLevel.Trace)
            VhLogger.Instance.LogTrace(GeneralEventId.Nat, "NatItem has been removed. {NatItem}", natItem2);
    }

    private ushort GetFreeNatId(IpVersion ipVersion, IpProtocol protocol)
    {
        var key = (ipVersion, protocol);

        // find last value
        lock (_lockObject) {
            if (!_lastNatIds.TryGetValue(key, out var lastNatId)) lastNatId = 8000;
            if (lastNatId > 0xFFFE) lastNatId = 0;

            for (var i = (ushort)(lastNatId + 1); i != lastNatId; i++) {
                if (i == 0) i++;
                if (!_map.ContainsKey((ipVersion, protocol, i))) {
                    _lastNatIds[key] = i;
                    return i;
                }
            }
        }

        throw new OverflowException("No more free NatId is available.");
    }

    /// <returns>null if not found</returns>
    public NatItem? Get(IpPacket ipPacket)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        var natItem = CreateNatItemFromPacket(ipPacket);
        lock (_lockObject) {
            if (!_mapR.TryGetValue(natItem, out var natItem2))
                return null;

            natItem2.AccessTime = FastDateTime.Now;
            return natItem2;
        }
    }

    public NatItem GetOrAdd(IpPacket ipPacket)
    {
        lock (_lockObject) {
            return Get(ipPacket) ?? Add(ipPacket);
        }
    }

    public NatItem Add(IpPacket ipPacket, bool overwrite = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        var natId = GetFreeNatId(ipPacket.Version, ipPacket.Protocol);
        return Add(ipPacket, natId, overwrite);
    }

    public NatItem Add(IpPacket ipPacket, ushort natId, bool overwrite = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        Cleanup();

        // try to find previous mapping
        var natItem = CreateNatItemFromPacket(ipPacket);
        natItem.NatId = natId;
        try {
            lock (_lockObject) {
                if (_disposed) throw new ObjectDisposedException(nameof(Nat));
                _map.Add((natItem.IpVersion, natItem.Protocol, natItem.NatId), natItem);
                _mapR.Add(natItem, natItem); //sounds crazy! because GetHashCode and Equals don't include all members
            }
        }
        catch (ArgumentException) when (overwrite) {
            Remove(natItem);
            lock (_lockObject) {
                if (_disposed) throw new ObjectDisposedException(nameof(Nat));
                _map.Add((natItem.IpVersion, natItem.Protocol, natItem.NatId), natItem);
                _mapR.Add(natItem, natItem); //sounds crazy! because GetHashCode and Equals don't include all members
            }
        }

        if (VhLogger.MinLogLevel <= LogLevel.Trace)
            VhLogger.Instance.LogTrace(GeneralEventId.Nat, "New NAT record. NatItem: {NatItem}", natItem);
        return natItem;
    }

    public void RemoveOldest(IpProtocol protocol)
    {
        lock (_lockObject) {
            var oldest = _map.Values.FirstOrDefault();
            if (oldest == null)
                return;

            foreach (var item in _map.Values.Where(x => x.Protocol == protocol)) {
                if (item.AccessTime < oldest.AccessTime)
                    oldest = item;
            }

            Remove(oldest);
        }
    }

    /// <returns>null if not found</returns>
    public NatItem? Resolve(IpVersion ipVersion, IpProtocol protocol, ushort id)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        lock (_lockObject) {
            var natKey = (ipVersion, protocol, id);
            if (!_map.TryGetValue(natKey, out var natItem))
                return null;

            natItem.AccessTime = FastDateTime.Now;
            return natItem;
        }
    }

    public void Dispose()
    {
        lock (_lockObject) {
            if (_disposed) return;
            _disposed = true;
        }

        // remove all
        NatItem[] items;
        lock (_lockObject)
            items = _mapR.Values.ToArray();

        foreach (var item in items)
            Remove(item);
    }
}