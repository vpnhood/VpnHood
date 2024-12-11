﻿using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Common.Collections;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Tunneling.Exceptions;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Tunneling;

public class UdpProxyPool : IPacketProxyPool, IJob
{
    private readonly IPacketProxyReceiver _packetProxyReceiver;
    private readonly ISocketFactory _socketFactory;
    private readonly int? _sendBufferSize;
    private readonly int? _receiveBufferSize;
    private readonly TimeoutDictionary<IPEndPoint, UdpProxy> _udpProxies;
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly int _maxClientCount;
    private bool _disposed;

    public int RemoteEndPointCount => _remoteEndPoints.Count;
    public int ClientCount => _udpProxies.Count;
    public JobSection JobSection { get; } = new();

    public UdpProxyPool(IPacketProxyReceiver packetProxyReceiver, ISocketFactory socketFactory,
        TimeSpan? udpTimeout, int? maxClientCount, LogScope? logScope = null,
        int? sendBufferSize = null, int? receiveBufferSize = null)
    {
        udpTimeout ??= TimeSpan.FromSeconds(120);
        maxClientCount ??= int.MaxValue;

        _packetProxyReceiver = packetProxyReceiver;
        _socketFactory = socketFactory;
        _sendBufferSize = sendBufferSize;
        _receiveBufferSize = receiveBufferSize;
        _maxClientCount = maxClientCount > 0
            ? maxClientCount.Value
            : throw new ArgumentException($"{nameof(maxClientCount)} must be greater than 0", nameof(maxClientCount));
        _udpProxies = new TimeoutDictionary<IPEndPoint, UdpProxy>(udpTimeout);
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(udpTimeout);
        _maxWorkerEventReporter = new EventReporter(VhLogger.Instance,
            "Session has reached to Maximum local UDP ports.", GeneralEventId.NetProtect, logScope: logScope);

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
        _remoteEndPoints.GetOrAdd(destinationEndPoint, _ => {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            _packetProxyReceiver.OnNewRemoteEndPoint(ProtocolType.Udp, destinationEndPoint);

        // find the proxy for the sourceEndPoint
        var udpProxy = _udpProxies.GetOrAdd(sourceEndPoint, key => {
            // check WorkerMaxCount
            if (_udpProxies.Count >= _maxClientCount) {
                _maxWorkerEventReporter.Raise();
                throw new UdpClientQuotaException(_udpProxies.Count);
            }

            isNewLocalEndPoint = true;
            return new UdpProxy(_packetProxyReceiver, CreateUdpClient(addressFamily), key);
        });

        // Raise new endpoint
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetProxyReceiver.OnNewEndPoint(ProtocolType.Udp,
                udpProxy.LocalEndPoint, destinationEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint);

        var dgram = udpPacket.PayloadData ?? [];
        return udpProxy.SendPacket(destinationEndPoint, dgram, noFragment);
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
        _disposed = true;

        _remoteEndPoints.Dispose();
        _maxWorkerEventReporter.Dispose();
    }
}