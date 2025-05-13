using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class PingProxyPool : IPacketProxyPool, IJob
{
    private bool _disposed;
    private readonly bool _autoDisposeSentPackets;
    private readonly IPacketProxyCallbacks? _packetProxyCallbacks;
    private readonly List<PingProxy> _pingProxies = [];
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly TimeSpan _workerTimeout = TimeSpan.FromMinutes(5);
    private readonly int _maxClientCount;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public int RemoteEndPointCount => _remoteEndPoints.Count;
    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(5));

    public PingProxyPool(PingProxyPoolOptions options)
    {
        _autoDisposeSentPackets = options.AutoDisposeSentPackets;
        _maxClientCount = options.MaxClientCount;
        _packetProxyCallbacks = options.PacketProxyCallbacks;
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(options.IcmpTimeout);
        _maxWorkerEventReporter = new EventReporter(VhLogger.Instance,
            "Session has reached to the maximum ping workers.", logScope: options.LogScope);

        JobRunner.Default.Add(this);
    }

    public int ClientCount {
        get {
            lock (_pingProxies)
                return _pingProxies.Count;
        }
    }

    private PingProxy GetFreePingProxy(out bool isNew)
    {
        lock (_pingProxies) {
            isNew = false;

            var pingProxy = _pingProxies.FirstOrDefault(x => !x.IsBusy);
            if (pingProxy != null)
                return pingProxy;

            if (_pingProxies.Count < _maxClientCount) {
                pingProxy = new PingProxy(_autoDisposeSentPackets);
                pingProxy.PacketReceived += PingProxy_PacketReceived;
                _pingProxies.Add(pingProxy);
                isNew = true;
                return pingProxy;
            }

            _maxWorkerEventReporter.Raise();

            pingProxy = _pingProxies.OrderBy(x => x.LastUsedTime).First();
            pingProxy.Cancel();
            return pingProxy;
        }
    }

    private void PingProxy_PacketReceived(object sender, PacketReceivedEventArgs e)
    {
        PacketReceived?.Invoke(this, e);
    }

    public void SendPacketQueued(IpPacket ipPacket)
    {
        try {
            // send packet via proxy
            PacketLogger.LogPacket(ipPacket, $"Delegating packet to host via {GetType().Name}.");
            SendPacketQueuedInternal(ipPacket);
        }
        catch (Exception ex) {
            // Log the error
            PacketLogger.LogPacket(ipPacket, $"Error while sending packet via {GetType().Name}.", exception: ex);

            // Dispose the packet if needed
            if (_autoDisposeSentPackets)
                ipPacket.Dispose();
        }
    }

    public void SendPacketQueuedInternal(IpPacket ipPacket)
    {
        if (ipPacket.Version == IpVersion.IPv4 && ipPacket.ExtractIcmpV4().Type != IcmpV4Type.EchoRequest)
            throw new NotSupportedException($"The icmp is not supported. Packet: {PacketLogger.Format(ipPacket)}.");

        if (ipPacket.Version == IpVersion.IPv6 && ipPacket.ExtractIcmpV6().Type != IcmpV6Type.EchoRequest)
            throw new NotSupportedException($"The icmp is not supported. Packet: {PacketLogger.Format(ipPacket)}.");

        var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, 0);
        var isNewRemoteEndPoint = false;

        // add the endpoint
        _remoteEndPoints.GetOrAdd(destinationEndPoint, _ => {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            _packetProxyCallbacks?.OnConnectionRequested(ipPacket.Protocol, destinationEndPoint);

        // send to a ping proxy
        var pingProxy = GetFreePingProxy(out var isNewLocalEndPoint);
        pingProxy.SendPacketQueued(ipPacket);

        // raise new endpoint event
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetProxyCallbacks?.OnConnectionEstablished(ipPacket.Protocol,
                new IPEndPoint(ipPacket.SourceAddress, 0), new IPEndPoint(ipPacket.DestinationAddress, 0),
                isNewLocalEndPoint, isNewRemoteEndPoint);

    }

    public Task RunJob()
    {
        lock (_pingProxies)
            TimeoutItemUtil.CleanupTimeoutList(_pingProxies, _workerTimeout);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_pingProxies) {
            foreach (var proxy in _pingProxies) {
                proxy.PacketReceived -= PingProxy_PacketReceived;
                proxy.Dispose();
            }
        }

        _maxWorkerEventReporter.Dispose();
        PacketReceived = null;
        JobRunner.Default.Remove(this);

        _disposed = true;
    }
}