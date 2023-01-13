using System.Net;
using PacketDotNet;
using VpnHood.Common.Collections;
using System.Threading.Tasks;
using VpnHood.Tunneling.Factory;
using System;
using VpnHood.Common.JobController;
using VpnHood.Tunneling.Exceptions;

namespace VpnHood.Tunneling;


public abstract class UdpProxyPool : IDisposable, IJob
{
    private readonly ISocketFactory _socketFactory;
    private readonly TimeoutDictionary<IPEndPoint, UdpProxy> _udpProxies = new();
    private TimeSpan _udpTimeout = TimeSpan.FromSeconds(120);
    private bool _disposed;

    public abstract Task OnPacketReceived(IPPacket packet);
    public TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> RemoteEndPoints { get; } = new(TimeSpan.FromSeconds(60));
    public int WorkerMaxCount { get; set; } = int.MaxValue;
    public int WorkerCount => _udpProxies.Count;
    public JobSection JobSection { get; } = new();
    public event EventHandler<EndPointEventArgs>? OnNewEndPoint;

    public TimeSpan UdpTimeout
    {
        get => _udpTimeout;
        set
        {
            _udpTimeout = value;
            RemoteEndPoints.Timeout = value;
            JobSection.Interval = value;
            _udpProxies.Timeout = value;
        }
    }

    protected UdpProxyPool(ISocketFactory socketFactory)
    {
        _socketFactory = socketFactory;
        JobRunner.Default.Add(this);
    }

    public Task SendPacket(IPAddress sourceAddress, IPAddress destinationAddress, UdpPacket udpPacket, bool? noFragment)
    {
        var sourceEndPoint = new IPEndPoint(sourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(destinationAddress, udpPacket.DestinationPort);
        var addressFamily = destinationAddress.AddressFamily;

        // find the proxy for the sourceEndPoint
        var isNewLocalEndPoint = false;
        var udpProxy = _udpProxies.GetOrAdd(sourceEndPoint, key =>
        {
            // check WorkerMaxCount
            if (_udpProxies.Count >= WorkerMaxCount)
                throw new UdpClientQuotaException(_udpProxies.Count);

            isNewLocalEndPoint = true;
            return new UdpProxy(this, _socketFactory.CreateUdpClient(addressFamily), sourceEndPoint);
        });


        // Add to RemoteEndPoints; DestinationEndPointMap may have duplicate RemoteEndPoints in different workers
        var isNewRemoteEndPoint = false;
        RemoteEndPoints.GetOrAdd(destinationEndPoint, _ =>
        {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });

        // Raise new endpoints
        OnNewEndPoint?.Invoke(this, new EndPointEventArgs(ProtocolType.Udp,
            udpProxy.LocalEndPoint, destinationEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint));

        var dgram = udpPacket.PayloadData ?? Array.Empty<byte>();
        return udpProxy.SendPacket(destinationEndPoint, dgram, noFragment);
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
        _disposed = true;

        RemoteEndPoints.Dispose();
    }
}