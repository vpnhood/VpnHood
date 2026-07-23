using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Exceptions;

namespace VpnHood.Core.Tunneling.Proxies;

public class UdpProxyPool : PassthroughPacketTransport, IPacketProxyPool
{
    private readonly IPacketProxyCallbacks? _packetProxyCallbacks;
    private readonly ISocketFactory _socketFactory;
    private readonly TransferBufferSize? _bufferSize;
    // the wrapper is what TimeoutDictionary disposes on removal; the proxy is shared across
    // connections and must only be disposed via _udpProxies
    private readonly TimeoutDictionary<ConnectionKey, TimeoutItem<(UdpProxy Proxy, TimeoutItem<IPEndPoint> Mapping)>>
        _connectionMap;
    private readonly List<UdpProxy> _udpProxies = [];
    // DNS workers are segregated from general UDP workers: DNS is one round trip, so its workers use a
    // small receive buffer and a short mapping life — neither must ever serve (or block) general flows.
    // Both lists are guarded by lock (_udpProxies)
    private readonly List<UdpProxy> _dnsProxies = [];
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly TimeSpan _udpTimeout;
    private readonly TimeSpan _dnsTimeout = TunnelDefaults.UdpDnsTimeout;
    private readonly int _maxClientCount;
    private readonly int _maxDnsClientCount;
    private readonly int _packetQueueCapacity;
    private readonly bool _autoDisposeSentPackets;
    private readonly Job _cleanupUdpWorkersJob;

    public int RemoteEndPointCount => _remoteEndPoints.Count;

    public int ClientCount {
        get {
            lock (_udpProxies) return _udpProxies.Count + _dnsProxies.Count;
        }
    }

    public UdpProxyPool(UdpProxyPoolOptions options)
    {
        _autoDisposeSentPackets = options.AutoDisposePackets;
        _packetProxyCallbacks = options.PacketProxyCallbacks;
        _socketFactory = options.SocketFactory;
        _packetQueueCapacity = options.PacketQueueCapacity;
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(options.UdpTimeout);
        _maxClientCount = options.MaxClientCount;
        _maxDnsClientCount = options.MaxDnsClientCount;
        _bufferSize = options.BufferSize;
        _maxWorkerEventReporter = new EventReporter("Session has reached to Maximum local UDP ports.",
            GeneralEventId.NetProtect, logScope: options.LogScope);

        _connectionMap = new TimeoutDictionary<ConnectionKey, TimeoutItem<(UdpProxy, TimeoutItem<IPEndPoint>)>>(options.UdpTimeout);
        _udpTimeout = options.UdpTimeout;
        // short-lived DNS workers must not linger until the general timeout fires
        var cleanupInterval = _dnsTimeout < _udpTimeout ? _dnsTimeout : _udpTimeout;
        _cleanupUdpWorkersJob = new Job(CleanupUdpWorkers, cleanupInterval, nameof(UdpProxyPool));
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        // send packet via proxy. The key is packed from the packet spans, so the hot path allocates
        // no string, IPAddress or IPEndPoint; endpoints are materialized only for new connections
        var udpPacket = ipPacket.ExtractUdp();
        var connectionKey = ConnectionKey.FromPacket(ipPacket, udpPacket);

        // A & C can share the same worker because their destinationEndPoint is different
        // A => D1
        // A => D2
        // B => D1
        // C => D3

        lock (_udpProxies) {
            // find the proxy for the connection (source-destination)
            if (!TryGetConnection(connectionKey, ipPacket, udpPacket, out var udpProxy))
                udpProxy = CreateConnection(connectionKey, ipPacket, udpPacket);

            udpProxy.SendPacketQueued(ipPacket);
        }
    }

    private bool TryGetConnection(ConnectionKey connectionKey, IpPacket ipPacket, UdpPacket udpPacket,
        [MaybeNullWhen(false)] out UdpProxy udpProxy)
    {
        udpProxy = null;
        if (!_connectionMap.TryGetValue(connectionKey, out var connection))
            return false;

        var (proxy, mapping) = connection.Value;
        if (proxy.IsDisposed)
            return false;

        // keep the reply mapping alive while the connection keeps sending; inbound packets are the only
        // other refresher, so an outbound-only flow would otherwise go deaf after UdpTimeout.
        // Re-add the mapping if the receive path has already removed it as expired
        if (!mapping.IsDisposed) {
            mapping.LastUsedTime = FastDateTime.Now;
        }
        else {
            var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
            mapping = new TimeoutItem<IPEndPoint>(new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort));
            if (!proxy.DestinationEndPointMap.TryAdd(destinationEndPoint, mapping))
                return false; // the destination has been remapped to another source; rebuild the connection

            connection.Value = (proxy, mapping);
        }

        udpProxy = proxy;
        return true;
    }

    private UdpProxy CreateConnection(ConnectionKey connectionKey, IpPacket ipPacket, UdpPacket udpPacket)
    {
        // per-connection path: endpoints are materialized here only
        var sourceEndPoint = new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
        var addressFamily = sourceEndPoint.AddressFamily;
        var isNewRemoteEndPoint = false;
        var isNewLocalEndPoint = false;

        // add the remote endpoint
        _remoteEndPoints.GetOrAdd(destinationEndPoint, _ => {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            _packetProxyCallbacks?.OnConnectionRequested(IpProtocol.Udp, destinationEndPoint.ToValue());

        // DNS workers live in their own list with their own quota, so a DNS burst can neither occupy
        // general workers nor be starved by them, and a general flow can never land on a small-buffer
        // short-lived DNS worker
        var isDns = udpPacket.DestinationPort == 53;
        var proxies = isDns ? _dnsProxies : _udpProxies;
        var proxyTimeout = isDns ? _dnsTimeout : _udpTimeout;

        // cleanup old workers
        TimeoutItemUtil.CleanupTimeoutList(proxies, proxyTimeout);

        // find a worker that does not map the destinationEndPoint yet
        UdpProxy? udpProxy = null;
        foreach (var proxy in proxies) {
            if (!proxy.IsDisposed && proxy.AddressFamily == addressFamily &&
                !proxy.DestinationEndPointMap.TryGetValue(destinationEndPoint, out _)) {
                udpProxy = proxy;
                break;
            }
        }

        // create a new worker if not found
        if (udpProxy == null) {
            // check WorkerMaxCount; DNS and general workers have independent quotas
            // (MaxDnsClientCount / MaxClientCount), so neither class can starve the other
            var maxCount = isDns ? _maxDnsClientCount : _maxClientCount;
            if (proxies.Count >= maxCount) {
                _maxWorkerEventReporter.Raise();
                throw new UdpClientQuotaException(proxies.Count);
            }

            // create a new worker
            udpProxy = new UdpProxy(CreateUdpSocket(addressFamily), proxyTimeout, _packetQueueCapacity,
                _autoDisposeSentPackets,
                isDns ? TunnelDefaults.UdpDnsBufferSize : TunnelDefaults.MaxUdpDatagramSize);
            udpProxy.PacketReceived += UdpProxy_OnPacketReceived;
            proxies.Add(udpProxy);
            isNewLocalEndPoint = true;

            // no event id on purpose: named event ids are dropped by the LogService event filter unless
            // explicitly enabled, and this line must reach Console.app during live diagnostics
            if (VhLogger.MinLogLevel <= LogLevel.Debug)
                VhLogger.Instance.LogDebug(
                    "[VH-UDP] Created UdpProxy. pool={Pool}, workers={WorkerCount}/{MaxWorkers}, footprint={Footprint:F1}MB, " +
                    "dstPort={DstPort}, {SourceEp} => {DestinationEp}",
                    isDns ? "dns" : "udp", proxies.Count, isDns ? _maxDnsClientCount : _maxClientCount,
                    Toolkit.Memory.VhMemory.Instance.GetInfo().ProcessFootprintMb ?? -1,
                    destinationEndPoint.Port, VhLogger.Format(sourceEndPoint), VhLogger.Format(destinationEndPoint));
        }

        // Add destinationEndPoint; a UdpProxy can not map a destinationEndPoint to more than one source endpoint.
        // Do not dispose the worker on failure; it is shared with other connections
        var mapping = new TimeoutItem<IPEndPoint>(sourceEndPoint);
        if (!udpProxy.DestinationEndPointMap.TryAdd(destinationEndPoint, mapping))
            throw new Exception($"Could not add {destinationEndPoint}.");

        // it is just for speed.
        _connectionMap.AddOrUpdate(connectionKey,
            new TimeoutItem<(UdpProxy, TimeoutItem<IPEndPoint>)>((udpProxy, mapping)));

        // Raise new endpoint
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetProxyCallbacks?.OnConnectionEstablished(IpProtocol.Udp,
                localEndPoint: udpProxy.LocalEndPoint.ToValue(),
                remoteEndPoint: destinationEndPoint.ToValue(),
                isNewLocalEndPoint: isNewLocalEndPoint,
                isNewRemoteEndPoint: isNewRemoteEndPoint);

        return udpProxy;
    }

    private void UdpProxy_OnPacketReceived(object? sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    private Socket CreateUdpSocket(AddressFamily addressFamily)
    {
        var socket = _socketFactory.CreateUdpSocket(addressFamily);
        if (_bufferSize?.Send > 0) socket.SendBufferSize = _bufferSize.Value.Send;
        if (_bufferSize?.Receive > 0) socket.ReceiveBufferSize = _bufferSize.Value.Receive;
        return socket;
    }

    private ValueTask CleanupUdpWorkers(CancellationToken cancellationToken)
    {
        // remove useless workers
        lock (_udpProxies) {
            TimeoutItemUtil.CleanupTimeoutList(_udpProxies, _udpTimeout);
            TimeoutItemUtil.CleanupTimeoutList(_dnsProxies, _dnsTimeout);
        }

        return default;
    }

    protected override void DisposeManaged()
    {
        lock (_udpProxies) {
            VhUtils.DisposeAll(_udpProxies);
            VhUtils.DisposeAll(_dnsProxies);
        }

        _cleanupUdpWorkersJob.Dispose();
        _connectionMap.Dispose();
        _remoteEndPoints.Dispose();
        _maxWorkerEventReporter.Dispose();

        base.DisposeManaged();
    }

    /// <summary>
    /// Allocation-free connection identity (family + source/destination address bytes + both ports),
    /// packed straight from the packet spans so the hot path never builds strings or endpoints.
    /// </summary>
    private readonly struct ConnectionKey(ulong srcLow, ulong srcHigh, ulong dstLow, ulong dstHigh,
        uint ports, byte version) : IEquatable<ConnectionKey>
    {
        private readonly ulong _srcLow = srcLow;
        private readonly ulong _srcHigh = srcHigh;
        private readonly ulong _dstLow = dstLow;
        private readonly ulong _dstHigh = dstHigh;
        private readonly uint _ports = ports;
        private readonly byte _version = version;

        public static ConnectionKey FromPacket(IpPacket ipPacket, UdpPacket udpPacket)
        {
            var src = ipPacket.SourceAddressSpan;
            var dst = ipPacket.DestinationAddressSpan;
            var ports = ((uint)udpPacket.SourcePort << 16) | udpPacket.DestinationPort;
            return src.Length == 4
                ? new ConnectionKey(
                    BinaryPrimitives.ReadUInt32LittleEndian(src), 0,
                    BinaryPrimitives.ReadUInt32LittleEndian(dst), 0,
                    ports, 4)
                : new ConnectionKey(
                    BinaryPrimitives.ReadUInt64LittleEndian(src),
                    BinaryPrimitives.ReadUInt64LittleEndian(src[8..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(dst),
                    BinaryPrimitives.ReadUInt64LittleEndian(dst[8..]),
                    ports, 6);
        }

        public bool Equals(ConnectionKey other) =>
            _ports == other._ports &&
            _dstLow == other._dstLow &&
            _srcLow == other._srcLow &&
            _srcHigh == other._srcHigh &&
            _dstHigh == other._dstHigh &&
            _version == other._version;

        public override bool Equals(object? obj) => obj is ConnectionKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(_srcLow, _srcHigh, _dstLow, _dstHigh, _ports, _version);
    }
}
