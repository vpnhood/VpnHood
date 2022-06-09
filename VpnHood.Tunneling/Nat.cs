using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling;

public class Nat : IDisposable
{
    private readonly bool _isDestinationSensitive;
    private readonly object _lockObject = new();
    private readonly Dictionary<(IPVersion, ProtocolType), ushort> _lastNatIds = new();
    private readonly Dictionary<(IPVersion, ProtocolType, ushort), NatItem> _map = new();
    private readonly Dictionary<NatItem, NatItem> _mapR = new();
    private bool _disposed;
    private DateTime _lastCleanupTime = DateTime.Now;

    public event EventHandler<NatEventArgs>? OnNatItemRemoved;

    public TimeSpan TcpTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public Nat(bool isDestinationSensitive)
    {
        _isDestinationSensitive = isDestinationSensitive;
    }

    public int ItemCount
    {
        get
        {
            lock (_lockObject)
                return _map.Count;
        }
    }

    public int GetItemCount(ProtocolType protocol)
    {
        lock (_lockObject)
            return _map.Count(x => x.Value.Protocol == protocol);
    }


    private NatItem CreateNatItemFromPacket(IPPacket ipPacket)
    {
        return _isDestinationSensitive ? new NatItemEx(ipPacket) : new NatItem(ipPacket);
    }

    private bool IsExpired(NatItem natItem)
    {
        if (natItem.Protocol == ProtocolType.Tcp)
            return DateTime.Now - natItem.AccessTime > TcpTimeout;
        if (natItem.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6)
            return DateTime.Now - natItem.AccessTime > IcmpTimeout;

        //treat other as UDP
        return DateTime.Now - natItem.AccessTime > UdpTimeout;
    }

    public void Cleanup()
    {
        if (DateTime.Now - _lastCleanupTime < IcmpTimeout)
            return;
        _lastCleanupTime = DateTime.Now;

        // select the expired items
        NatItem[] items;
        lock (_lockObject)
            items = _mapR.Values.Where(IsExpired).ToArray();

        foreach (var item in items)
            Remove(item);
    }

    private void Remove(NatItem natItem)
    {
        NatItem natItem2;
        lock (_lockObject)
        {
            _mapR.Remove(natItem, out natItem2);
            _map.Remove((natItem.IpVersion, natItem.Protocol, natItem.NatId), out _);
        }

        VhLogger.Instance.LogTrace(GeneralEventId.Nat, $"NatItem has been removed. {natItem2}");
        OnNatItemRemoved?.Invoke(this, new NatEventArgs(natItem2));
    }
    private ushort GetFreeNatId(IPVersion ipVersion, ProtocolType protocol)
    {
        var key = (ipVersion, protocol);

        // find last value
        lock (_lockObject)
        {
            if (!_lastNatIds.TryGetValue(key, out var lastNatId)) lastNatId = 8000;
            if (lastNatId > 0xFFFE) lastNatId = 0;

            for (var i = (ushort)(lastNatId + 1); i != lastNatId; i++)
            {
                if (i == 0) i++;
                if (!_map.ContainsKey((ipVersion, protocol, i)))
                {
                    _lastNatIds[key] = i;
                    return i;
                }
            }
        }

        throw new OverflowException("No more free NatId is available.");
    }

    /// <returns>null if not found</returns>
    public NatItem? Get(IPPacket ipPacket)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        var natItem = CreateNatItemFromPacket(ipPacket);
        lock (_lockObject)
        {
            if (!_mapR.TryGetValue(natItem, out var natItem2))
                return null;

            natItem2.AccessTime = DateTime.Now;
            return natItem2;
        }
    }

    public NatItem GetOrAdd(IPPacket ipPacket)
    {
        return Get(ipPacket) ?? Add(ipPacket);
    }

    public NatItem Add(IPPacket ipPacket, bool overwrite = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        var natId = GetFreeNatId(ipPacket.Version, ipPacket.Protocol);
        return Add(ipPacket, natId, overwrite);
    }

    public NatItem Add(IPPacket ipPacket, ushort natId, bool overwrite = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        Cleanup();

        // try to find previous mapping
        var natItem = CreateNatItemFromPacket(ipPacket);
        natItem.NatId = natId;
        try
        {
            lock (_lockObject)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Nat));
                _map.Add((natItem.IpVersion, natItem.Protocol, natItem.NatId), natItem);
                _mapR.Add(natItem, natItem); //sound crazy! because GetHashCode and Equals don't include all members
            }
        }
        catch (ArgumentException) when (overwrite)
        {
            Remove(natItem);
            lock (_lockObject)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Nat));
                _map.Add((natItem.IpVersion, natItem.Protocol, natItem.NatId), natItem);
                _mapR.Add(natItem, natItem); //sound crazy! because GetHashCode and Equals don't include all members
            }
        }

        VhLogger.Instance.LogTrace(GeneralEventId.Nat, $"New NAT record. {natItem}");
        return natItem;
    }

    public void RemoveOldest(ProtocolType protocol)
    {
        NatItem oldest;
        lock (_lockObject)
        {
            oldest = _map.Values.FirstOrDefault();
            if (oldest == null)
                return;

            foreach (var item in _map.Values.Where(x => x.Protocol == protocol))
            {
                if (item.AccessTime < oldest.AccessTime)
                    oldest = item;
            }
        }

        Remove(oldest);
    }

    /// <returns>null if not found</returns>
    public NatItem? Resolve(IPVersion ipVersion, ProtocolType protocol, ushort id)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Nat));

        lock (_lockObject)
        {
            var natKey = (ipVersion, protocol, id);
            if (!_map.TryGetValue(natKey, out var natItem))
                return null;

            natItem.AccessTime = DateTime.Now;
            return natItem;
        }
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
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