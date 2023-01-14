using System.Net;
using PacketDotNet;
using VpnHood.Common.Collections;
using System.Threading.Tasks;
using VpnHood.Tunneling.Factory;
using System;
using VpnHood.Common.JobController;
using VpnHood.Tunneling.Exceptions;
using VpnHood.Common.Utils;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling;


public abstract class UdpProxyPool : IDisposable, IJob
{
    private readonly ISocketFactory _socketFactory;
    private readonly TimeoutDictionary<IPEndPoint, UdpProxy> _udpProxies = new();
    private TimeSpan _udpTimeout = TimeSpan.FromSeconds(120);
    private bool _disposed;
    private readonly EventReporter _maxUdpCountEventReporter;
    
    public abstract Task OnPacketReceived(IPPacket packet);
    public TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> RemoteEndPoints { get; } = new(TimeSpan.FromSeconds(60));
    public int WorkerMaxCount { get; set; } = int.MaxValue;
    public int WorkerCount => _udpProxies.Count;
    public JobSection JobSection { get; } = new();
    public event EventHandler<EndPointEventPairArgs>? OnNewEndPointEstablished;
    public event EventHandler<EndPointEventArgs>? OnNewRemoteEndPoint;


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
        _maxUdpCountEventReporter = new EventReporter(VhLogger.Instance, "Session has reached to Maximum local UDP ports.");
        JobRunner.Default.Add(this);
    }

    public Task SendPacket(IPAddress sourceAddress, IPAddress destinationAddress, UdpPacket udpPacket, bool? noFragment)
    {
        var sourceEndPoint = new IPEndPoint(sourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(destinationAddress, udpPacket.DestinationPort);
        var addressFamily = sourceAddress.AddressFamily;
        var isNewRemoteEndPoint = false;
        var isNewLocalEndPoint = false;

        // add the remote endpoint
        RemoteEndPoints.GetOrAdd(destinationEndPoint, (_) =>
        {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            OnNewRemoteEndPoint?.Invoke(this, new EndPointEventArgs(ProtocolType.Udp, destinationEndPoint));

        // find the proxy for the sourceEndPoint
        var udpProxy = _udpProxies.GetOrAdd(sourceEndPoint, key =>
        {
            // check WorkerMaxCount
            if (_udpProxies.Count >= WorkerMaxCount)
            {
                _maxUdpCountEventReporter.Raised();
                throw new UdpClientQuotaException(_udpProxies.Count);
            }

            isNewLocalEndPoint = true;
            return new UdpProxy(this, _socketFactory.CreateUdpClient(addressFamily), key);
        });

        // Raise new endpoints
        OnNewEndPointEstablished?.Invoke(this, new EndPointEventPairArgs(ProtocolType.Udp,
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