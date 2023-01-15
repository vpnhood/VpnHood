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
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints = new(TimeSpan.FromSeconds(60));
    private readonly EventReporter _maxUdpCountEventReporter;
    private bool _disposed;

    public int MaxLocalEndPointCount { get; set; } = int.MaxValue;
    public int RemoteEndPointCount => _remoteEndPoints.Count;
    public int LocalEndPointCount => _udpProxies.Count;
    public JobSection JobSection { get; } = new();

    public UdpProxyPool(ISocketFactory socketFactory, IPacketProxyReceiver packetProxyReceiver, TimeSpan? udpTimeout)
    {
        udpTimeout ??= TimeSpan.FromSeconds(120);

        _packetProxyReceiver = packetProxyReceiver;
        _socketFactory = socketFactory;
        _maxUdpCountEventReporter = new EventReporter(VhLogger.Instance, "Session has reached to Maximum local UDP ports.");
        _remoteEndPoints.Timeout = udpTimeout;
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
            if (_udpProxies.Count >= MaxLocalEndPointCount)
            {
                _maxUdpCountEventReporter.Raised();
                throw new UdpClientQuotaException(_udpProxies.Count);
            }

            isNewLocalEndPoint = true;
            return new UdpProxy(_packetProxyReceiver, _socketFactory.CreateUdpClient(addressFamily), key);
        });

        // Raise new endpoints
        if (isNewLocalEndPoint)
            _packetProxyReceiver.OnNewLocalEndPoint(ProtocolType.Udp, udpProxy.LocalEndPoint);

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
    }
}