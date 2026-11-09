using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Collections;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Forwarding;

public class PingForwarderPool : PassthroughPacketTransport, IPacketForwarderPool
{
    private readonly IPacketForwarderCallbacks? _packetForwarderCallbacks;
    private readonly List<PingForwarder> _pingForwarders = [];
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly TimeoutDictionary<IpEndPointValue, TimeoutItem<bool>> _remoteEndPoints;
    private readonly TimeSpan _workerTimeout = TimeSpan.FromMinutes(5);
    private readonly int _maxClientCount;
    private readonly Job _cleanupJob;
    public DateTime LastUsedTime { get; set; }

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

    private PingForwarder GetFreePingForwarder(out bool isNew)
    {
        lock (_pingForwarders) {
            isNew = false;

            var pingForwarder = _pingForwarders.FirstOrDefault(x => !x.IsSending);
            if (pingForwarder != null)
                return pingForwarder;

            if (_pingForwarders.Count < _maxClientCount) {
                pingForwarder = new PingForwarder();
                pingForwarder.PacketReceived += PingForwarder_PacketReceived;
                _pingForwarders.Add(pingForwarder);
                isNew = true;
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
        var isNewRemoteEndPoint = false;

        // add the endpoint
        _remoteEndPoints.GetOrAdd(destinationEndPoint, _ => {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            _packetForwarderCallbacks?.OnConnectionRequested(ipPacket.Protocol, destinationEndPoint);

        // raise new endpoint event before the packet goes: once handed on, it is the forwarder's, which may have sent and
        // disposed it already
        var pingForwarder = GetFreePingForwarder(out var isNewLocalEndPoint);
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetForwarderCallbacks?.OnConnectionEstablished(ipPacket.Protocol,
                new IpEndPointValue(ipPacket.SourceAddressValue, 0), destinationEndPoint,
                isNewLocalEndPoint, isNewRemoteEndPoint);

        // send to a ping forwarder
        pingForwarder.SendPacketQueued(ipPacket);
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        lock (_pingForwarders)
            TimeoutItemUtil.CleanupTimeoutList(_pingForwarders, _workerTimeout);

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