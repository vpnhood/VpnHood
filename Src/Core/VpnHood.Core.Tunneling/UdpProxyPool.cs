using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Tunneling;

public class UdpProxyPool : IPacketProxyPool, IJob
{
    private readonly IPacketProxyCallbacks? _packetProxyCallbacks;
    private readonly ISocketFactory _socketFactory;
    private readonly int? _sendBufferSize;
    private readonly int? _receiveBufferSize;
    private readonly int _packetQueueCapacity;
    private readonly TimeoutDictionary<IPEndPoint, UdpProxy> _udpProxies;
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly int _maxClientCount;
    private bool _disposed;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public int RemoteEndPointCount => _remoteEndPoints.Count;
    public int ClientCount => _udpProxies.Count;
    public JobSection JobSection { get; } = new();

    public UdpProxyPool(UdpProxyPoolOptions options)
    {
        var udpTimeout = options.UdpTimeout ?? TunnelDefaults.UdpTimeout;

        _packetProxyCallbacks = options.PacketProxyCallbacks;
        _socketFactory = options.SocketFactory;
        _packetQueueCapacity = options.PacketQueueCapacity ?? TunnelDefaults.ProxyPacketQueueCapacity;
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(udpTimeout);
        _maxClientCount = options.MaxClientCount ?? TunnelDefaults.MaxUdpClientCount;
        _sendBufferSize = options.SendBufferSize;
        _receiveBufferSize = options.ReceiveBufferSize;
        _maxWorkerEventReporter = new EventReporter(VhLogger.Instance,
            "Session has reached to Maximum local UDP ports.", GeneralEventId.NetProtect, logScope: options.LogScope);

        _udpProxies = new TimeoutDictionary<IPEndPoint, UdpProxy>(udpTimeout);
        JobSection.Interval = udpTimeout;
        JobRunner.Default.Add(this);
    }

    public void SendPacketQueued(IpPacket ipPacket)
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
            var ret = new UdpProxy(CreateUdpClient(addressFamily), key, _packetQueueCapacity);
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

    private void UdpProxy_OnPacketReceived(object sender, PacketReceivedEventArgs e)
    {
        PacketReceived?.Invoke(this, e);
    }

    private UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = _socketFactory.CreateUdpClient(addressFamily);
        if (_sendBufferSize.HasValue) udpClient.Client.SendBufferSize = _sendBufferSize.Value;
        if (_receiveBufferSize.HasValue) udpClient.Client.ReceiveBufferSize = _receiveBufferSize.Value;
        return udpClient;
    }

    public Task RunJob()
    {
        // remove useless workers
        _udpProxies.Cleanup();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        PacketReceived = null;
        _remoteEndPoints.Dispose();
        _maxWorkerEventReporter.Dispose();
        _disposed = true;
    }
}