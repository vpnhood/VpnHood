using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Collections;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Forwarding;

internal class PingForwarderPool : PassthroughPacketTransport, IPacketForwarderPool
{
    private readonly IPacketForwarderCallbacks? _packetForwarderCallbacks;
    private readonly List<PingForwarder> _pingForwarders = [];
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly TimeoutDictionary<IpEndPointValue, TimeoutItem<bool>> _remoteEndPoints;
    private readonly TimeSpan _workerTimeout = TimeSpan.FromMinutes(5);
    private readonly int _maxClientCount;
    private readonly Job _cleanupJob;

    public int RemoteEndPointCount => _remoteEndPoints.Count;

    public PingForwarderPool(PingForwarderPoolOptions options)
    {
        _maxClientCount = options.MaxClientCount;
        _packetForwarderCallbacks = options.PacketForwarderCallbacks;
        _remoteEndPoints = new TimeoutDictionary<IpEndPointValue, TimeoutItem<bool>>(options.IcmpTimeout);
        _maxWorkerEventReporter =
            new EventReporter("Session has reached to the maximum ping workers.", logScope: options.LogScope);
        _cleanupJob = new Job(Cleanup, nameof(PingForwarderPool));
    }

    public int ClientCount {
        get {
            lock (_pingForwarders)
                return _pingForwarders.Count;
        }
    }

    // the caller holds the lock of the list
    private PingForwarder GetFreePingForwarder()
    {
        {
            var pingForwarder = _pingForwarders.FirstOrDefault(x => !x.IsSending);
            if (pingForwarder != null)
                return pingForwarder;

            if (_pingForwarders.Count < _maxClientCount) {
                pingForwarder = new PingForwarder();
                pingForwarder.PacketReceived += PingForwarder_PacketReceived;
                _pingForwarders.Add(pingForwarder);
                return pingForwarder;
            }

            _maxWorkerEventReporter.Raise();

            pingForwarder = _pingForwarders.OrderBy(x => x.PacketStat.LastActivityTime).First();
            pingForwarder.Cancel();
            return pingForwarder;
        }
    }

    private void PingForwarder_PacketReceived(object? sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        if (ipPacket.Version == IpVersion.IPv4 && ipPacket.ExtractIcmpV4().Type != IcmpV4Type.EchoRequest)
            throw new NotSupportedException($"The icmp is not supported. Packet: {PacketLogger.Format(ipPacket)}.");

        if (ipPacket.Version == IpVersion.IPv6 && ipPacket.ExtractIcmpV6().Type != IcmpV6Type.EchoRequest)
            throw new NotSupportedException($"The icmp is not supported. Packet: {PacketLogger.Format(ipPacket)}.");

        var destinationEndPoint = new IpEndPointValue(ipPacket.DestinationAddressValue, 0);

        // A new destination is checked before it is remembered: remembered first, one the check refused, as the server's
        // net-scan check does by a throw, passed unchecked with the next packet
        var isNewRemoteEndPoint = !_remoteEndPoints.TryGetValue(destinationEndPoint, out _);
        if (isNewRemoteEndPoint) {
            _packetForwarderCallbacks?.OnConnectionRequested(ipPacket.Protocol, destinationEndPoint);
            _remoteEndPoints.GetOrAdd(destinationEndPoint, static _ => new TimeoutItem<bool>(true),
                out isNewRemoteEndPoint);

            // before the hand-off, while the packet is still the caller's: a throw after it had its caller dispose a
            // packet the worker reads. A ping has no socket of its own, so no local endpoint to report
            if (isNewRemoteEndPoint)
                _packetForwarderCallbacks?.OnConnectionEstablished(ipPacket.Protocol, localEndPoint: null,
                    destinationEndPoint, isNewLocalEndPoint: false, isNewRemoteEndPoint: true);
        }

        // handed on under the lock that picks the worker, which the sweep and the close take too: a worker picked is not
        // swept before it has the packet, and none is made after the close, which disposes those it finds
        lock (_pingForwarders) {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            GetFreePingForwarder().SendPacketQueued(ipPacket);
        }
    }

    // A worker is swept once it has gone unused for its time, by its own record of when it last sent or received, and
    // never while it has a ping to send or to wait for
    internal void Cleanup(DateTime now)
    {
        lock (_pingForwarders) {
            for (var i = _pingForwarders.Count - 1; i >= 0; i--) {
                var forwarder = _pingForwarders[i];
                if (!forwarder.IsDisposed &&
                    (forwarder.IsSending || now - forwarder.PacketStat.LastActivityTime <= _workerTimeout))
                    continue;

                forwarder.PacketReceived -= PingForwarder_PacketReceived;
                forwarder.Dispose();
                _pingForwarders.RemoveAt(i);
            }
        }
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        Cleanup(FastDateTime.UtcNow);
        return default;
    }

    protected override void DisposeManaged()
    {
        lock (_pingForwarders)
            foreach (var forwarder in _pingForwarders) {
                forwarder.PacketReceived -= PingForwarder_PacketReceived;
                forwarder.Dispose();
            }

        _cleanupJob.Dispose();
        _maxWorkerEventReporter.Dispose();
        _remoteEndPoints.Dispose();

        base.DisposeManaged();
    }
}