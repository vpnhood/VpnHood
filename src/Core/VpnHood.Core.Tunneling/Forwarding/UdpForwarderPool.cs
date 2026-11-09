using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Collections;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Forwarding;

public class UdpForwarderPool : PassthroughPacketTransport, IPacketForwarderPool
{
    private readonly IPacketForwarderCallbacks? _packetForwarderCallbacks;
    private readonly ISocketFactory _socketFactory;
    private readonly TransferBufferSize? _bufferSize;
    // the wrapper is what TimeoutDictionary disposes on removal; the forwarder is shared across
    // connections and must only be disposed via _udpForwarders
    private readonly TimeoutDictionary<ConnectionKey, TimeoutItem<(UdpForwarder Forwarder, TimeoutItem<IPEndPoint> Mapping)>>
        _connectionMap;
    private readonly List<UdpForwarder> _udpForwarders = [];
    // DNS workers are segregated from general UDP workers: DNS is one round trip, so its workers use a
    // small receive buffer and a short mapping life — neither must ever serve (or block) general flows.
    // Both lists are guarded by lock (_udpForwarders)
    private readonly List<UdpForwarder> _dnsForwarders = [];
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly TimeSpan _udpTimeout;
    private readonly TimeSpan _dnsTimeout = TransportDefaults.UdpDnsTimeout;
    private readonly int _maxClientCount;
    private readonly int _maxDnsClientCount;
    private readonly int _packetQueueCapacity;
    private readonly Job _cleanupUdpWorkersJob;

    public int RemoteEndPointCount => _remoteEndPoints.Count;

    public int ClientCount {
        get {
            lock (_udpForwarders) return _udpForwarders.Count + _dnsForwarders.Count;
        }
    }

    public UdpForwarderPool(UdpForwarderPoolOptions options)
    {
        _packetForwarderCallbacks = options.PacketForwarderCallbacks;
        _socketFactory = options.SocketFactory;
        _packetQueueCapacity = options.PacketQueueCapacity;
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(options.UdpTimeout);
        _maxClientCount = options.MaxClientCount;
        _maxDnsClientCount = options.MaxDnsClientCount;
        _bufferSize = options.BufferSize;
        _maxWorkerEventReporter = new EventReporter("Session has reached to Maximum local UDP ports.",
            GeneralEventId.NetProtect, logScope: options.LogScope);

        _connectionMap = new TimeoutDictionary<ConnectionKey, TimeoutItem<(UdpForwarder, TimeoutItem<IPEndPoint>)>>(options.UdpTimeout);
        _udpTimeout = options.UdpTimeout;
        // short-lived DNS workers must not linger until the general timeout fires
        var cleanupInterval = _dnsTimeout < _udpTimeout ? _dnsTimeout : _udpTimeout;
        _cleanupUdpWorkersJob = new Job(CleanupUdpWorkers, cleanupInterval, nameof(UdpForwarderPool));
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        // send packet via forwarder. The key is packed from the packet spans, so the hot path allocates
        // no string, IPAddress or IPEndPoint; endpoints are materialized only for new connections
        var udpPacket = ipPacket.ExtractUdp();
        var connectionKey = ConnectionKey.FromPacket(ipPacket, udpPacket);

        // A & C can share the same worker because their destinationEndPoint is different
        // A => D1
        // A => D2
        // B => D1
        // C => D3

        lock (_udpForwarders) {
            // find the forwarder for the connection (source-destination)
            if (!TryGetConnection(connectionKey, ipPacket, udpPacket, out var udpForwarder))
                udpForwarder = CreateConnection(connectionKey, ipPacket, udpPacket);

            udpForwarder.SendPacketQueued(ipPacket);
        }
    }

    private bool TryGetConnection(ConnectionKey connectionKey, IpPacket ipPacket, UdpPacket udpPacket,
        [MaybeNullWhen(false)] out UdpForwarder udpForwarder)
    {
        udpForwarder = null;
        if (!_connectionMap.TryGetValue(connectionKey, out var connection))
            return false;

        var (forwarder, mapping) = connection.Value;
        if (forwarder.IsDisposed)
            return false;

        // keep the reply mapping alive while the connection keeps sending; inbound packets are the only
        // other refresher, so an outbound-only flow would otherwise go deaf after UdpTimeout.
        // Re-add the mapping if the receive path has already removed it as expired
        if (!mapping.IsDisposed) {
            mapping.LastUsedTime = FastDateTime.UtcNow;
        }
        else {
            var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
            mapping = new TimeoutItem<IPEndPoint>(new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort));
            if (!forwarder.DestinationEndPointMap.TryAdd(destinationEndPoint, mapping))
                return false; // the destination has been remapped to another source; rebuild the connection

            connection.Value = (forwarder, mapping);
        }

        udpForwarder = forwarder;
        return true;
    }

    private UdpForwarder CreateConnection(ConnectionKey connectionKey, IpPacket ipPacket, UdpPacket udpPacket)
    {
        // per-connection path: endpoints are materialized here only
        var sourceEndPoint = new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
        var addressFamily = sourceEndPoint.AddressFamily;
        var isNewLocalEndPoint = false;

        // add the remote endpoint
        _remoteEndPoints.GetOrAdd(destinationEndPoint, static _ => new TimeoutItem<bool>(true),
            out var isNewRemoteEndPoint);
        if (isNewRemoteEndPoint)
            _packetForwarderCallbacks?.OnConnectionRequested(IpProtocol.Udp, destinationEndPoint.ToValue());

        // DNS workers live in their own list with their own quota, so a DNS burst can neither occupy
        // general workers nor be starved by them, and a general flow can never land on a small-buffer
        // short-lived DNS worker
        var isDns = udpPacket.DestinationPort == 53;
        var forwarders = isDns ? _dnsForwarders : _udpForwarders;
        var forwarderTimeout = isDns ? _dnsTimeout : _udpTimeout;

        // cleanup old workers
        TimeoutItemUtil.CleanupTimeoutList(forwarders, forwarderTimeout);

        // find a worker that does not map the destinationEndPoint yet
        UdpForwarder? udpForwarder = null;
        foreach (var forwarder in forwarders) {
            if (!forwarder.IsDisposed && forwarder.AddressFamily == addressFamily &&
                !forwarder.DestinationEndPointMap.TryGetValue(destinationEndPoint, out _)) {
                udpForwarder = forwarder;
                break;
            }
        }

        // create a new worker if not found
        if (udpForwarder == null) {
            // check WorkerMaxCount; DNS and general workers have independent quotas
            // (MaxDnsClientCount / MaxClientCount), so neither class can starve the other
            var maxCount = isDns ? _maxDnsClientCount : _maxClientCount;
            if (forwarders.Count >= maxCount) {
                _maxWorkerEventReporter.Raise();
                throw new UdpClientQuotaException(forwarders.Count);
            }

            // create a new worker
            udpForwarder = new UdpForwarder(CreateUdpSocket(addressFamily), forwarderTimeout, _packetQueueCapacity,
                isDns ? TransportDefaults.UdpDnsBufferSize : TransportDefaults.MaxUdpDatagramSize);
            udpForwarder.PacketReceived += UdpForwarder_OnPacketReceived;
            forwarders.Add(udpForwarder);
            isNewLocalEndPoint = true;

            // this line must reach Console.app during live diagnostics
            if (VhLogger.MinLogLevel <= LogLevel.Debug)
                VhLogger.Instance.LogDebug(
                    "[VH-UDP] Created UdpForwarder. pool={Pool}, workers={WorkerCount}/{MaxWorkers}, footprint={Footprint:F1}MB, " +
                    "dstPort={DstPort}, {SourceEp} => {DestinationEp}",
                    isDns ? "dns" : "udp", forwarders.Count, isDns ? _maxDnsClientCount : _maxClientCount,
                    VhMemory.Instance.GetInfo().ProcessFootprintMb ?? -1,
                    destinationEndPoint.Port, VhLogger.Format(sourceEndPoint), VhLogger.Format(destinationEndPoint));
        }

        // Add destinationEndPoint; a UdpForwarder can not map a destinationEndPoint to more than one source endpoint.
        // Do not dispose the worker on failure; it is shared with other connections
        var mapping = new TimeoutItem<IPEndPoint>(sourceEndPoint);
        if (!udpForwarder.DestinationEndPointMap.TryAdd(destinationEndPoint, mapping))
            throw new Exception($"Could not add {destinationEndPoint}.");

        // it is just for speed.
        _connectionMap.AddOrUpdate(connectionKey,
            new TimeoutItem<(UdpForwarder, TimeoutItem<IPEndPoint>)>((udpForwarder, mapping)));

        // Raise new endpoint
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetForwarderCallbacks?.OnConnectionEstablished(IpProtocol.Udp,
                localEndPoint: udpForwarder.LocalEndPoint.ToValue(),
                remoteEndPoint: destinationEndPoint.ToValue(),
                isNewLocalEndPoint: isNewLocalEndPoint,
                isNewRemoteEndPoint: isNewRemoteEndPoint);

        return udpForwarder;
    }

    private void UdpForwarder_OnPacketReceived(object? sender, IpPacket ipPacket)
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
        lock (_udpForwarders) {
            TimeoutItemUtil.CleanupTimeoutList(_udpForwarders, _udpTimeout);
            TimeoutItemUtil.CleanupTimeoutList(_dnsForwarders, _dnsTimeout);
        }

        return default;
    }

    protected override void DisposeManaged()
    {
        lock (_udpForwarders) {
            VhUtils.DisposeAll(_udpForwarders);
            VhUtils.DisposeAll(_dnsForwarders);
        }

        _cleanupUdpWorkersJob.Dispose();
        _connectionMap.Dispose();
        _remoteEndPoints.Dispose();
        _maxWorkerEventReporter.Dispose();

        base.DisposeManaged();
    }

    /// <summary>
    /// Allocation-free connection identity: the source and destination addresses as values, which carry their IP
    /// version, and both ports, read straight from the packet so the hot path never builds strings or endpoints.
    /// </summary>
    private readonly struct ConnectionKey(IpAddressValue source, IpAddressValue destination, uint ports)
        : IEquatable<ConnectionKey>
    {
        private readonly IpAddressValue _source = source;
        private readonly IpAddressValue _destination = destination;
        private readonly uint _ports = ports;

        public static ConnectionKey FromPacket(IpPacket ipPacket, UdpPacket udpPacket) => new(
            ipPacket.SourceAddressValue, ipPacket.DestinationAddressValue,
            ((uint)udpPacket.SourcePort << 16) | udpPacket.DestinationPort);

        public bool Equals(ConnectionKey other) =>
            _ports == other._ports &&
            _destination == other._destination &&
            _source == other._source;

        public override bool Equals(object? obj) => obj is ConnectionKey other && Equals(other);

        // a value's own hash takes each 32-bit word on its own, so a client cannot pick destinations that collide in
        // this table whatever the seed
        public override int GetHashCode() => HashCode.Combine(_source, _destination, _ports);
    }
}
