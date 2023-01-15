using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Server.Exceptions;
using VpnHood.Tunneling.Factory;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Tunneling;

public abstract class ProxyManager : IPacketProxyReceiver, IDisposable
{
    private readonly IPAddress[] _blockList =
    {
        IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
    };

    private bool _disposed;
    private readonly HashSet<IChannel> _channels = new();
    private readonly PingProxyPool _pingProxyPool = new();
    private readonly UdpProxyPool _udpProxyPool;

    public int UdpClientMaxCount { get => _udpProxyPool.MaxLocalEndPointCount; set => _udpProxyPool.MaxLocalEndPointCount = value; }
    public int UdpClientCount => _udpProxyPool.LocalEndPointCount;
    public int PingClientMaxCount { get => _pingProxyPool.WorkerMaxCount; set => _pingProxyPool.WorkerMaxCount = value; }
    public int PingClientCount => _pingProxyPool.WorkerCount;

    public TimeSpan IcmpTimeout
    {
        get => _pingProxyPool.IcmpTimeout;
        set => _pingProxyPool.IcmpTimeout = value;
    }

    public int UdpConnectionCount => _udpProxyPool.LocalEndPointCount;

    // ReSharper disable once UnusedMember.Global
    public int TcpConnectionCount { get { lock (_channels) return _channels.Count(x => x is not IDatagramChannel); } }
    public abstract Task OnPacketReceived(IPPacket ipPacket);
    public virtual void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint) { }
    public virtual void OnNewLocalEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint? remoteEndPoint) { }
    protected abstract bool IsPingSupported { get; }

    protected ProxyManager(ISocketFactory socketFactory, ProxyManagerOptions options)
    {
        _udpProxyPool = new UdpProxyPool(socketFactory, this, options.UdpTimeout);
        //_pingProxyPool.OnEndPointEstablished += OnNewEndPointEstablished;
        //_pingProxyPool.OnNewRemoteEndPoint += OnNewRemoteEndPoint;
    }

    public void SendPacket(IPPacket[] ipPackets)
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

        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6)
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
            await _udpProxyPool.SendPacket(ipPacket);
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
}