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
    private readonly PingProxyPool _pingProxyPool;
    private readonly UdpProxyPool _udpProxyPool;

    public int UdpClientCount => _udpProxyPool.ClientCount;
    public int PingClientCount => _pingProxyPool.ClientCount;

    // ReSharper disable once UnusedMember.Global
    public int TcpConnectionCount { get { lock (_channels) return _channels.Count(x => x is not IDatagramChannel); } }
    public abstract Task OnPacketReceived(IPPacket ipPacket);
    public virtual void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint) { }
    public virtual void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, bool isNewLocalEndPoint, bool isNewRemoteEndPoint) { }
    protected abstract bool IsPingSupported { get; }

    protected ProxyManager(ISocketFactory socketFactory, ProxyManagerOptions options)
    {
        _udpProxyPool = new UdpProxyPool(this, socketFactory, options.UdpTimeout, options.MaxUdpWorkerCount);
        _pingProxyPool = new PingProxyPool(this, options.IcmpTimeout);
    }

    public void SendPacket(IPPacket[] ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            _ = SendPacket(ipPacket);
    }

    public async Task SendPacket(IPPacket ipPacket)
    {
        if (ipPacket is null)
            throw new ArgumentNullException(nameof(ipPacket));

        // drop blocked packets
        if (_blockList.Any(x => x.Equals(ipPacket.DestinationAddress)))
            return;

        // send packet via proxy
        if (VhLogger.IsDiagnoseMode)
            PacketUtil.LogPacket(ipPacket, "Delegating packet to host via proxy.");

        try
        {
            switch (ipPacket.Protocol)
            {
                case ProtocolType.Udp:
                    await _udpProxyPool.SendPacket(ipPacket);
                    break;

                case ProtocolType.Icmp or ProtocolType.IcmpV6:
                    if (!IsPingSupported)
                        throw new NotSupportedException("Ping is not supported by this proxy.");

                    await SendIcmpPacket(ipPacket);
                    break;

                default:
                    throw new Exception($"{ipPacket.Protocol} packet should not be sent through this channel.");
            }
        }
        catch (Exception ex) when (ex is ISelfLog)
        {
        }
        catch (Exception ex)
        {
            PacketUtil.LogPacket(ipPacket, "Error in delegating packet via proxy.", LogLevel.Error, ex);
        }
    }

    private async Task SendIcmpPacket(IPPacket ipPacket)
{
    // validations
    if (ipPacket is null)
        throw new ArgumentNullException(nameof(ipPacket));

    if (!IsPingSupported)
        throw new NotSupportedException("Ping is not supported by this proxy.");

    try
    {
        // send packet via proxy
        if (VhLogger.IsDiagnoseMode)
            PacketUtil.LogPacket(ipPacket, "Delegating packet to host via proxy.");

        await _pingProxyPool.SendPacket(ipPacket);
    }
    catch (Exception ex)
    {
        if (VhLogger.IsDiagnoseMode && ex is not ISelfLog)
            PacketUtil.LogPacket(ipPacket, "Error in delegating packet via proxy.", LogLevel.Error, ex);
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
        VhLogger.Instance.LogError(ipPacket.Protocol.ToString(), ex,
            "Could not send a packet. Packet: {Packet}", VhLogger.FormatIpPacket(ipPacket.ToString()));
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