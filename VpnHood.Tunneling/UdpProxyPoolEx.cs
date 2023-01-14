using System.Net;
using PacketDotNet;
using VpnHood.Common.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using VpnHood.Tunneling.Factory;
using System.Linq;
using System;
using VpnHood.Tunneling.Exceptions;
using VpnHood.Common.JobController;

namespace VpnHood.Tunneling;


public abstract class UdpProxyPoolEx : IDisposable, IJob
{
    private readonly ISocketFactory _socketFactory;
    private readonly TimeoutDictionary<string, UdpProxyEx> _connectionMap;
    private readonly List<UdpProxyEx> _udpProxies = new();
    private TimeSpan _udpTimeout = TimeSpan.FromSeconds(120);
    private bool _disposed;

    public abstract Task OnPacketReceived(IPPacket packet);
    public TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> RemoteEndPoints { get; } = new(TimeSpan.FromSeconds(60));
    public int WorkerMaxCount { get; set; } = int.MaxValue;
    public int WorkerCount { get { lock (_udpProxies) return _udpProxies.Count; } }
    public JobSection JobSection { get; } = new();
    public event EventHandler<EndPointEventPairArgs>? OnNewEndPointEstablished;
    public event EventHandler<EndPointEventArgs>? OnNewRemoteEndPoint;

    public TimeSpan UdpTimeout
    {
        get => _udpTimeout;
        set
        {
            _udpTimeout = value;
            _connectionMap.Timeout = value;
            RemoteEndPoints.Timeout = value;
            JobSection.Interval = value;
        }
    }

    protected UdpProxyPoolEx(ISocketFactory socketFactory)
    {
        _socketFactory = socketFactory;
        _connectionMap = new TimeoutDictionary<string, UdpProxyEx>(UdpTimeout);
        JobRunner.Default.Add(this);
    }

    public Task SendPacket(IPAddress sourceAddress, IPAddress destinationAddress, UdpPacket udpPacket, bool? noFragment)
    {
        var sourceEndPoint = new IPEndPoint(sourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(destinationAddress, udpPacket.DestinationPort);
        var addressFamily = destinationAddress.AddressFamily;

        // find the proxy for the connection (source-destination)
        var connectionKey = $"{sourceEndPoint}:{destinationEndPoint}";
        var udpProxy = _connectionMap.GetOrAdd(connectionKey, _ =>
        {
            // add the remote endpoint
            var isNewRemoteEndPoint = false;
            RemoteEndPoints.GetOrAdd(destinationEndPoint, (_) =>
            {
                isNewRemoteEndPoint = true;
                return new TimeoutItem<bool>(true);
            });
            if (isNewRemoteEndPoint)
                OnNewRemoteEndPoint?.Invoke(this, new EndPointEventArgs(ProtocolType.Udp, destinationEndPoint));

            // Find or create a worker that does not use the RemoteEndPoint
            lock (_udpProxies)
            {
                var newUdpProxy = _udpProxies.FirstOrDefault(x =>
                    x.AddressFamily == addressFamily &&
                    !x.DestinationEndPointMap.TryGetValue(destinationEndPoint, out var _));

                var isNewLocalEndPoint = false;
                if (newUdpProxy == null)
                {
                    // check WorkerMaxCount
                    if (_udpProxies.Count >= WorkerMaxCount)
                        throw new UdpClientQuotaException(_udpProxies.Count);

                    newUdpProxy = new UdpProxyEx(this, _socketFactory.CreateUdpClient(addressFamily), addressFamily);
                    _udpProxies.Add(newUdpProxy);
                    isNewLocalEndPoint = true;
                }

                // Raise new endpoints
                OnNewEndPointEstablished?.Invoke(this, new EndPointEventPairArgs(ProtocolType.Udp,
                    newUdpProxy.LocalEndPoint, destinationEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint));

                // Add destinationEndPoint; each newUdpWorker can not map a destinationEndPoint to more than one source port
                newUdpProxy.DestinationEndPointMap.TryAdd(destinationEndPoint, new TimeoutItem<IPEndPoint>(sourceEndPoint));
                return newUdpProxy;
            }
        });

        var dgram = udpPacket.PayloadData ?? Array.Empty<byte>();
        return udpProxy.SendPacket(destinationEndPoint, dgram, noFragment);
    }

    public Task RunJob()
    {
        // remove useless workers
        lock (_udpProxies)
            TimeoutItemUtil.CleanupTimeoutList(_udpProxies, UdpTimeout);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock(_udpProxies)
            _udpProxies.ForEach(udpWorker => udpWorker.Dispose());

        _connectionMap.Dispose();
        RemoteEndPoints.Dispose();
    }
}