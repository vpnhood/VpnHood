using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Tunneling.Proxies;

public class UdpProxyPool : PassthroughPacketTransport, IPacketProxyPool
{
    private readonly bool _autoDisposeSentPackets;
    private readonly IPacketProxyCallbacks? _packetProxyCallbacks;
    private readonly ISocketFactory _socketFactory;
    private readonly int? _sendBufferSize;
    private readonly int? _receiveBufferSize;
    private readonly int _packetQueueCapacity;
    private readonly TimeoutDictionary<IPEndPoint, UdpProxy> _udpProxies;
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly int _maxClientCount;
    private readonly Job _cleanupUdpJob;

    public int RemoteEndPointCount => _remoteEndPoints.Count;
    public int ClientCount => _udpProxies.Count;

    public UdpProxyPool(UdpProxyPoolOptions options)
    {
        _autoDisposeSentPackets = options.AutoDisposePackets;
        _packetProxyCallbacks = options.PacketProxyCallbacks;
        _socketFactory = options.SocketFactory;
        _packetQueueCapacity = options.PacketQueueCapacity;
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(options.UdpTimeout);
        _maxClientCount = options.MaxClientCount;
        _sendBufferSize = options.SendBufferSize;
        _receiveBufferSize = options.ReceiveBufferSize;
        _maxWorkerEventReporter = new EventReporter("Session has reached to Maximum local UDP ports.", GeneralEventId.NetProtect, logScope: options.LogScope);

        _udpProxies = new TimeoutDictionary<IPEndPoint, UdpProxy>(options.UdpTimeout);
        _cleanupUdpJob = new Job(CleanupUdpWorkers, options.UdpTimeout, nameof(UdpProxyPool));
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        // send packet via proxy
        var udpPacket = ipPacket.ExtractUdp();
        var sourceEndPoint = new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
        var addressFamily = ipPacket.SourceAddress.AddressFamily;
        var isNewRemoteEndPoint = false;
        var isNewLocalEndPoint = false;

        // add the remote endpoint
        _remoteEndPoints.GetOrAdd(destinationEndPoint, _ => {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            _packetProxyCallbacks?.OnConnectionRequested(IpProtocol.Udp, destinationEndPoint);

        // find the proxy for the sourceEndPoint
        var udpProxy = _udpProxies.GetOrAdd(sourceEndPoint, key => {
            // check WorkerMaxCount
            if (_udpProxies.Count >= _maxClientCount) {
                _maxWorkerEventReporter.Raise();
                throw new UdpClientQuotaException(_udpProxies.Count);
            }

            isNewLocalEndPoint = true;
            var ret = new UdpProxy(CreateUdpClient(addressFamily), key, _packetQueueCapacity, _autoDisposeSentPackets);
            ret.PacketReceived += UdpProxy_OnPacketReceived;
            return ret;
        });

        // Raise new endpoint
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetProxyCallbacks?.OnConnectionEstablished(IpProtocol.Udp,
                localEndPoint: udpProxy.LocalEndPoint,
                remoteEndPoint: destinationEndPoint,
                isNewLocalEndPoint: isNewLocalEndPoint,
                isNewRemoteEndPoint: isNewRemoteEndPoint);

        udpProxy.SendPacketQueued(ipPacket);
    }

    private void UdpProxy_OnPacketReceived(object sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    private UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = _socketFactory.CreateUdpClient(addressFamily);
        if (_sendBufferSize.HasValue) udpClient.Client.SendBufferSize = _sendBufferSize.Value;
        if (_receiveBufferSize.HasValue) udpClient.Client.ReceiveBufferSize = _receiveBufferSize.Value;
        return udpClient;
    }

    private ValueTask CleanupUdpWorkers(CancellationToken cancellationToken)
    {
        // remove useless workers
        _udpProxies.Cleanup();
        return default;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _remoteEndPoints.Dispose();
            _maxWorkerEventReporter.Dispose();
            _udpProxies.Dispose();
            _cleanupUdpJob.Dispose();
        }

        base.Dispose(disposing);
    }
}