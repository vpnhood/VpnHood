using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Transports;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Tunneling.Proxies;

public class UdpProxyPoolEx : PacketChannelPipe, IPacketProxyPool, IJob
{
    private readonly IPacketProxyCallbacks? _packetProxyCallbacks;
    private readonly ISocketFactory _socketFactory;
    private readonly int? _sendBufferSize;
    private readonly int? _receiveBufferSize;
    private readonly TimeoutDictionary<string, UdpProxyEx> _connectionMap;
    private readonly List<UdpProxyEx> _udpProxies = [];
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly TimeSpan _udpTimeout;
    private readonly int _maxClientCount;
    private readonly int _packetQueueCapacity;
    private readonly bool _autoDisposeSentPackets;

    public int RemoteEndPointCount => _remoteEndPoints.Count;

    public int ClientCount {
        get {
            lock (_udpProxies) return _udpProxies.Count;
        }
    }

    public JobSection JobSection { get; } = new();

    public UdpProxyPoolEx(UdpProxyPoolOptions options)
        : base(options.AutoDisposePackets)
    {
        _autoDisposeSentPackets = options.AutoDisposePackets;
        _packetProxyCallbacks = options.PacketProxyCallbacks;
        _socketFactory = options.SocketFactory;
        _packetQueueCapacity = options.PacketQueueCapacity;
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(options.UdpTimeout);
        _maxClientCount = options.MaxClientCount;
        _sendBufferSize = options.SendBufferSize;
        _receiveBufferSize = options.ReceiveBufferSize;
        _maxWorkerEventReporter = new EventReporter(VhLogger.Instance,
            "Session has reached to Maximum local UDP ports.", GeneralEventId.NetProtect, logScope: options.LogScope);

        _connectionMap = new TimeoutDictionary<string, UdpProxyEx>(options.UdpTimeout);
        _udpTimeout = options.UdpTimeout;

        JobSection.Interval = options.UdpTimeout;
        JobRunner.Default.Add(this);
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

        // find the proxy for the connection (source-destination)
        var connectionKey = $"{sourceEndPoint}:{destinationEndPoint}";
        if (!_connectionMap.TryGetValue(connectionKey, out var udpProxy)) {

            // add the remote endpoint
            _remoteEndPoints.GetOrAdd(destinationEndPoint, _ => {
                isNewRemoteEndPoint = true;
                return new TimeoutItem<bool>(true);
            });
            if (isNewRemoteEndPoint)
                _packetProxyCallbacks?.OnConnectionRequested(IpProtocol.Udp, destinationEndPoint);

            lock (_udpProxies) {
                // find the proxy for the sourceEndPoint
                udpProxy = _udpProxies.FirstOrDefault(x =>
                    x.AddressFamily == addressFamily &&
                    !x.DestinationEndPointMap.TryGetValue(destinationEndPoint, out _));

                // create a new worker if not found
                if (udpProxy == null) {
                    // check WorkerMaxCount
                    if (_udpProxies.Count >= _maxClientCount) {
                        _maxWorkerEventReporter.Raise();
                        throw new UdpClientQuotaException(_udpProxies.Count);
                    }

                    // create a new worker
                    udpProxy = new UdpProxyEx(CreateUdpClient(addressFamily), _udpTimeout, 
                        _packetQueueCapacity, _autoDisposeSentPackets);
                    udpProxy.PacketReceived += UdpProxy_OnPacketReceived;

                    _udpProxies.Add(udpProxy);
                    isNewLocalEndPoint = true;
                }

                // Add destinationEndPoint; a new UdpWorker can not map a destinationEndPoint to more than one source port
                if (!udpProxy.DestinationEndPointMap.TryAdd(destinationEndPoint, new TimeoutItem<IPEndPoint>(sourceEndPoint))) {
                    udpProxy.Dispose();
                    throw new Exception($"Could not add {destinationEndPoint}.");
                }

                // it is just for speed. if failed, it will be added again
                if (!_connectionMap.TryAdd(connectionKey, udpProxy))
                    VhLogger.Instance.LogWarning($"Could not add {connectionKey} to the connection map. It is already added.");
            }
        }

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
        OnPacketReceived(e);
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
        lock (_udpProxies)
            TimeoutItemUtil.CleanupTimeoutList(_udpProxies, _udpTimeout);

        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            // Dispose managed resources
            lock (_udpProxies)
                _udpProxies.ForEach(udpWorker => udpWorker.Dispose());

            _connectionMap.Dispose();
            _remoteEndPoints.Dispose();
            _maxWorkerEventReporter.Dispose();
            JobRunner.Default.Remove(this);
        }

        base.Dispose(disposing);
    }
}