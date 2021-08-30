using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class Nat : IDisposable
    {
        private readonly Dictionary<(ProtocolType, ushort), NatItem> _map = new();
        private readonly Dictionary<NatItem, NatItem> _mapR = new();
        private readonly Dictionary<ProtocolType, ushort> _lastNatdIds = new();
        private readonly bool _isDestinationSensitive;
        private const int _tcpLlifeTimeSeconds = 15 * 60; //15 min for TCP
        private const int _udpLlifeTimeSeconds = 05 * 60; // 5 min for UDP
        private DateTime _lastCleanupTime = DateTime.Now;
        bool _disposed = false;

        public NatItem[] Items => _map.Select(x => x.Value).ToArray();
        public event EventHandler<NatEventArgs>? OnNatItemRemoved;

        public Nat(bool isDestinationSensitive)
        {
            _isDestinationSensitive = isDestinationSensitive;
        }

        private NatItem CreateNatItemFromPacket(IPPacket ipPacket) => _isDestinationSensitive ? new NatItemEx(ipPacket) : new NatItem(ipPacket);

        private bool IsExpired(NatItem natItem)
        {
            if (natItem.Protocol == ProtocolType.Tcp)
                return (DateTime.Now - natItem.AccessTime).TotalSeconds > _tcpLlifeTimeSeconds;
            else
                return (DateTime.Now - natItem.AccessTime).TotalSeconds > _udpLlifeTimeSeconds;
        }

        private void Cleanup()
        {
            if ((DateTime.Now - _lastCleanupTime).TotalSeconds < _udpLlifeTimeSeconds)
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

        private readonly object _lockObject = new();
        private ushort GetFreeNatId(ProtocolType protocol)
        {
            // find last value
            if (!_lastNatdIds.TryGetValue(protocol, out var lastNatId)) lastNatId = 8000;
            if (lastNatId > 0xFFFF) lastNatId = 0;

            for (var i = (ushort)(lastNatId + 1); i != lastNatId; i++)
            {
                if (i == 0) i++;
                if (!_map.ContainsKey((protocol, i)))
                {
                    _lastNatdIds[protocol] = i;
                    return i;
                }
            }

            throw new OverflowException("No more free NatId is available!");
        }

        /// <returns>null if not found</returns>
        public NatItem? Get(IPPacket ipPacket)
        {
            if (_disposed) throw new ObjectDisposedException(typeof(Nat).Name);

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
            return Get(ipPacket) ?? Add(ipPacket, false);
        }

        public NatItem Add(IPPacket ipPacket, bool overwrite = false)
        {
            if (_disposed) throw new ObjectDisposedException(typeof(Nat).Name);

            lock (_lockObject)
            {
                var natId = GetFreeNatId(ipPacket.Protocol);
                return Add(ipPacket, natId, overwrite);
            }
        }

        public NatItem Add(IPPacket ipPacket, ushort natId, bool overwrite = false)
        {
            if (_disposed) throw new ObjectDisposedException(typeof(Nat).Name);

            lock (_lockObject)
            {
                Cleanup();

                // try to find previous mapping
                var natItem = CreateNatItemFromPacket(ipPacket);
                natItem.NatId = natId;
                try
                {
                    _map.Add((natItem.Protocol, natItem.NatId), natItem);
                    _mapR.Add(natItem, natItem); //sound crazy! because GetHashCode and Equals don't incluse all members
                }
                catch (ArgumentException) when (overwrite)
                {
                    Remove(natItem);
                    _map.Add((natItem.Protocol, natItem.NatId), natItem);
                    _mapR.Add(natItem, natItem); //sound crazy! because GetHashCode and Equals don't incluse all members
                }

                VhLogger.Instance.LogTrace(GeneralEventId.Nat, $"New NAT record. {natItem}");
                return natItem;
            }
        }


        /// <returns>null if not found</returns>
        public NatItem? Resolve(ProtocolType protocol, ushort id)
        {
            if (_disposed) throw new ObjectDisposedException(typeof(Nat).Name);

            lock (_lockObject)
            {
                if (!_map.TryGetValue((protocol, id), out var natItem))
                    return null;

                natItem.AccessTime = DateTime.Now;
                return natItem;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // remove all
                foreach (var item in _mapR.ToArray()) //To array is required to prevent modification of source in for each
                    Remove(item.Value);

                _disposed = true;
            }
        }
    }

}
