using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public class Nat : IDisposable
    {
        private readonly TimeSpan _tcpTimeout = TimeSpan.FromMinutes(15);
        private readonly TimeSpan _udpTimeout = TimeSpan.FromMinutes(5);
        private readonly bool _isDestinationSensitive;
        private readonly Dictionary<ProtocolType, ushort> _lastNatIds = new();

        private readonly object _lockObject = new();
        private readonly Dictionary<(ProtocolType, ushort), NatItem> _map = new();
        private readonly Dictionary<NatItem, NatItem> _mapR = new();
        private bool _disposed;
        private DateTime _lastCleanupTime = DateTime.Now;

        public Nat(bool isDestinationSensitive)
        {
            _isDestinationSensitive = isDestinationSensitive;
        }

        public NatItem[] Items => _map.Select(x => x.Value).ToArray();

        public void Dispose()
        {
            if (!_disposed)
            {
                // remove all
                foreach (var item in
                    _mapR.ToArray()) //To array is required to prevent modification of source in for each
                    Remove(item.Value);

                _disposed = true;
            }
        }

        public event EventHandler<NatEventArgs>? OnNatItemRemoved;

        private NatItem CreateNatItemFromPacket(IPPacket ipPacket)
        {
            return _isDestinationSensitive ? new NatItemEx(ipPacket) : new NatItem(ipPacket);
        }

        private bool IsExpired(NatItem natItem)
        {
            if (natItem.Protocol == ProtocolType.Tcp)
                return DateTime.Now - natItem.AccessTime > _tcpTimeout;
            return DateTime.Now - natItem.AccessTime > _udpTimeout;
        }

        private void Cleanup()
        {
            if (DateTime.Now - _lastCleanupTime < _udpTimeout)
                return;
            _lastCleanupTime = DateTime.Now;

            // select the expired items
            var itemsToRemove = _mapR.Where(x => IsExpired(x.Value));
            foreach (var item in itemsToRemove)
                Remove(item.Value);
        }

        private void Remove(NatItem natItem)
        {
            _mapR.Remove(natItem, out _);
            _map.Remove((natItem.Protocol, natItem.NatId), out _);

            VhLogger.Instance.LogTrace(GeneralEventId.Nat, $"NatItem has been removed. {natItem}");
            OnNatItemRemoved?.Invoke(this, new NatEventArgs(natItem));
        }

        private ushort GetFreeNatId(ProtocolType protocol)
        {
            // find last value
            if (!_lastNatIds.TryGetValue(protocol, out var lastNatId)) lastNatId = 8000;
            if (lastNatId > 0xFFFF) lastNatId = 0;

            for (var i = (ushort) (lastNatId + 1); i != lastNatId; i++)
            {
                if (i == 0) i++;
                if (!_map.ContainsKey((protocol, i)))
                {
                    _lastNatIds[protocol] = i;
                    return i;
                }
            }

            throw new OverflowException("No more free NatId is available!");
        }

        /// <returns>null if not found</returns>
        public NatItem? Get(IPPacket ipPacket)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Nat));

            lock (_lockObject)
            {
                // try to find previous mapping
                var natItem = CreateNatItemFromPacket(ipPacket);

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

            lock (_lockObject)
            {
                var natId = GetFreeNatId(ipPacket.Protocol);
                return Add(ipPacket, natId, overwrite);
            }
        }

        public NatItem Add(IPPacket ipPacket, ushort natId, bool overwrite = false)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Nat));

            lock (_lockObject)
            {
                Cleanup();

                // try to find previous mapping
                var natItem = CreateNatItemFromPacket(ipPacket);
                natItem.NatId = natId;
                try
                {
                    _map.Add((natItem.Protocol, natItem.NatId), natItem);
                    _mapR.Add(natItem, natItem); //sound crazy! because GetHashCode and Equals don't include all members
                }
                catch (ArgumentException) when (overwrite)
                {
                    Remove(natItem);
                    _map.Add((natItem.Protocol, natItem.NatId), natItem);
                    _mapR.Add(natItem, natItem); //sound crazy! because GetHashCode and Equals don't include all members
                }

                VhLogger.Instance.LogTrace(GeneralEventId.Nat, $"New NAT record. {natItem}");
                return natItem;
            }
        }


        /// <returns>null if not found</returns>
        public NatItem? Resolve(ProtocolType protocol, ushort id)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Nat));

            lock (_lockObject)
            {
                if (!_map.TryGetValue((protocol, id), out var natItem))
                    return null;

                natItem.AccessTime = DateTime.Now;
                return natItem;
            }
        }
    }
}