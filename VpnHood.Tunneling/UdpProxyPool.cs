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

public class UdpProxyPool : IPacketProxyPool, IJob
{
    private readonly IPacketProxyReceiver _packetProxyReceiver;
    private readonly ISocketFactory _socketFactory;
    private readonly TimeoutDictionary<IPEndPoint, UdpProxy> _udpProxies = new();
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly int _maxClientCount;
    private bool _disposed;

    public int RemoteEndPointCount => _remoteEndPoints.Count;
    public int ClientCount => _udpProxies.Count;
    public JobSection JobSection { get; } = new();

    public UdpProxyPool(IPacketProxyReceiver packetProxyReceiver, ISocketFactory socketFactory, 
        TimeSpan? udpTimeout, int? maxClientCount)
    {
        udpTimeout ??= TimeSpan.FromSeconds(120);

        _packetProxyReceiver = packetProxyReceiver;
        _socketFactory = socketFactory;
        _maxClientCount = maxClientCount ?? int.MaxValue;
        _maxWorkerEventReporter = new EventReporter(VhLogger.Instance, "Session has reached to Maximum local UDP ports.");
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(udpTimeout);
        _udpProxies.Timeout = udpTimeout;

        JobSection.Interval = udpTimeout.Value;
        JobRunner.Default.Add(this);
    }

    public Task SendPacket(IPPacket ipPacket)
    {
        // send packet via proxy
        var udpPacket = PacketUtil.ExtractUdp(ipPacket);
        bool? noFragment = ipPacket.Protocol == ProtocolType.IPv6 && ipPacket is IPv4Packet ipV4Packet
            ? (ipV4Packet.FragmentFlags & 0x2) != 0
            : null;

        var sourceEndPoint = new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
        var addressFamily = ipPacket.SourceAddress.AddressFamily;
        var isNewRemoteEndPoint = false;
        var isNewLocalEndPoint = false;

        // add the remote endpoint
        _remoteEndPoints.GetOrAdd(destinationEndPoint, (_) =>
        {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            _packetProxyReceiver.OnNewRemoteEndPoint(ProtocolType.Udp, destinationEndPoint);

        // find the proxy for the sourceEndPoint
        var udpProxy = _udpProxies.GetOrAdd(sourceEndPoint, key =>
        {
            // check WorkerMaxCount
            if (_udpProxies.Count >= _maxClientCount)
            {
                _maxWorkerEventReporter.Raised();
                throw new UdpClientQuotaException(_udpProxies.Count);
            }

            isNewLocalEndPoint = true;
            return new UdpProxy(_packetProxyReceiver, _socketFactory.CreateUdpClient(addressFamily), key);
        });

        // Raise new endpoint
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetProxyReceiver.OnNewEndPoint(ProtocolType.Udp, 
                udpProxy.LocalEndPoint, destinationEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint);

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

        _remoteEndPoints.Dispose();
        _maxWorkerEventReporter.Dispose();
    }
}