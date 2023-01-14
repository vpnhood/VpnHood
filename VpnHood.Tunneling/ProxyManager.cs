using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Server.Exceptions;
using VpnHood.Tunneling.Exceptions;
using VpnHood.Tunneling.Factory;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Tunneling;


public abstract class ProxyManager : IDisposable
{
    private readonly IPAddress[] _blockList =
    {
        IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
    };

    private bool _disposed;
    private readonly HashSet<IChannel> _channels = new();
    private readonly PingProxyPool _pingProxyPool = new();
    private readonly MyUdpProxyPool _udpProxyPool;

    public event EventHandler<EndPointEventPairArgs>? OnNewEndPointEstablished;
    public event EventHandler<EndPointEventArgs>? OnNewRemoteEndPoint;

    public int UdpClientMaxCount { get => _udpProxyPool.WorkerMaxCount; set => _udpProxyPool.WorkerMaxCount = value; }
    public int UdpClientCount => _udpProxyPool.WorkerCount;
    public int PingClientMaxCount { get => _pingProxyPool.WorkerMaxCount; set => _pingProxyPool.WorkerMaxCount = value; }
    public int PingClientCount => _pingProxyPool.WorkerCount;

    public TimeSpan UdpTimeout
    {
        get => _udpProxyPool.UdpTimeout;
        set => _udpProxyPool.UdpTimeout = value;
    }

    public TimeSpan IcmpTimeout
    {
        get => _pingProxyPool.IcmpTimeout;
        set => _pingProxyPool.IcmpTimeout = value;
    }


    public TimeSpan TcpTimeout { get; set; } = TunnelUtil.TcpTimeout;

    public int UdpConnectionCount => _udpProxyPool.WorkerCount;

    // ReSharper disable once UnusedMember.Global
    public int TcpConnectionCount { get { lock (_channels) return _channels.Count(x => x is not IDatagramChannel); } }
    protected abstract Task OnPacketReceived(IPPacket ipPacket);
    protected abstract bool IsPingSupported { get; }

    protected ProxyManager(ISocketFactory socketFactory)
    {
        _udpProxyPool = new MyUdpProxyPool(this, socketFactory);
        _udpProxyPool.OnNewEndPointEstablished += OnNewEndPointEstablished;
        _udpProxyPool.OnNewRemoteEndPoint += OnNewRemoteEndPoint;
        _pingProxyPool.OnEndPointEstablished += OnNewEndPointEstablished;
        _pingProxyPool.OnNewRemoteEndPoint += OnNewRemoteEndPoint;
    }

    public virtual void SendPacket(IPPacket[] ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            SendPacket(ipPacket);
    }

    public bool SendPacket(IPPacket ipPacket)
    {
        if (ipPacket is null)
            throw new ArgumentNullException(nameof(ipPacket));

        // drop blocked packets
        if (_blockList.Any(x => x.Equals(ipPacket.DestinationAddress)))
            return false;

        if (ipPacket.Protocol == ProtocolType.Udp)
            _ = SendUdpPacket(ipPacket);

        else if (ipPacket.Protocol == ProtocolType.Icmp)
            _ = SendIcmpPacket(ipPacket);

        else if (ipPacket.Protocol == ProtocolType.IcmpV6)
            _ = SendIcmpPacket(ipPacket);

        else if (ipPacket.Protocol == ProtocolType.Tcp)
            throw new Exception("Tcp Packet should not be sent through this channel! Use TcpProxy.");

        else
            throw new Exception($"{ipPacket.Protocol} is not supported!");

        return true;
    }

    private async Task SendIcmpPacket(IPPacket ipPacket)
    {
        // validations
        if (ipPacket is null)
            throw new ArgumentNullException(nameof(ipPacket));

        if ((ipPacket.Version != IPVersion.IPv4 || ipPacket.Extract<IcmpV4Packet>()?.TypeCode != IcmpV4TypeCode.EchoRequest) &&
            (ipPacket.Version != IPVersion.IPv6 || ipPacket.Extract<IcmpV6Packet>()?.Type != IcmpV6Type.EchoRequest))
            throw new NotSupportedException($"The icmp is not supported. Packet: {PacketUtil.Format(ipPacket)}.");

        if (!IsPingSupported)
            throw new NotSupportedException("Ping is not supported by this proxy.");

        try
        {
            // send packet via proxy
            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPacket(ipPacket, "Delegating packet to host via proxy.");

            var retPacket = await _pingProxyPool.Send(ipPacket);

            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPacket(ipPacket, "Delegating packet to client via proxy.");

            await OnPacketReceived(retPacket);
        }
        catch (Exception ex)
        {
            if (VhLogger.IsDiagnoseMode && ex is not ISelfLog)
                PacketUtil.LogPacket(ipPacket, "Error in delegating echo packet via proxy.", LogLevel.Error, ex);
        }
    }

    private async Task SendUdpPacket(IPPacket ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        try
        {
            // send packet via proxy
            var udpPacket = PacketUtil.ExtractUdp(ipPacket);
            bool? noFragment = ipPacket.Protocol == ProtocolType.IPv6 && ipPacket is IPv4Packet ipV4Packet
                ? (ipV4Packet.FragmentFlags & 0x2) != 0
                : null;

            await _udpProxyPool.SendPacket(ipPacket.SourceAddress, ipPacket.DestinationAddress, udpPacket, noFragment);
        }
        catch (Exception ex) when (ex is ISelfLog)
        {
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(GeneralEventId.Udp, ex,
                "Could not send a UDP packet. Packet: {Packet}", VhLogger.FormatIpPacket(ipPacket.ToString()));
        }
    }

    public void AddChannel(IChannel channel)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProxyManager));

        channel.OnFinished += Channel_OnFinished;
        lock (_channels)
            _channels.Add(channel);
        channel.Start();
    }

    private void Channel_OnFinished(object sender, ChannelEventArgs e)
    {
        e.Channel.OnFinished -= Channel_OnFinished;
        lock (_channels)
            _channels.Remove(e.Channel);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _udpProxyPool.Dispose();
        _pingProxyPool.Dispose();

        // dispose channels
        lock (_channels)
        {
            foreach (var channel in _channels)
                channel.Dispose();
        }
    }

    private class MyUdpProxyPool : UdpProxyPool
    {
        private readonly ProxyManager _proxyManager;

        public MyUdpProxyPool(ProxyManager proxyManager, ISocketFactory socketFactory)
            : base(socketFactory)
        {
            _proxyManager = proxyManager;
        }

        public override Task OnPacketReceived(IPPacket ipPacket)
        {
            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPacket(ipPacket, "Delegating packet to client via proxy.");

            return _proxyManager.OnPacketReceived(ipPacket);
        }
    }
}