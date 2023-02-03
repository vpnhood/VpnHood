using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
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
    private readonly IPacketProxyPool _pingProxyPool;
    private readonly IPacketProxyPool _udpProxyPool;

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
        _pingProxyPool = new PingProxyPool(this, options.IcmpTimeout, logScope: options.LogScope);
        _udpProxyPool = options.UseUdpProxy2
            ? new UdpProxyPoolEx(this, socketFactory, options.UdpTimeout, options.MaxUdpWorkerCount, logScope: options.LogScope)
            : new UdpProxyPool(this, socketFactory, options.UdpTimeout, options.MaxUdpWorkerCount, logScope: options.LogScope);
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

                    await _pingProxyPool.SendPacket(ipPacket);
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