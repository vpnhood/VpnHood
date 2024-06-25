using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Channels;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Tunneling;

public abstract class ProxyManager : IPacketProxyReceiver
{
    private readonly IPAddress[] _blockList =
    [
        IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
    ];

    private bool _disposed;
    private readonly HashSet<IChannel> _channels = [];
    private readonly IPacketProxyPool _pingProxyPool;
    private readonly IPacketProxyPool _udpProxyPool;

    [SuppressMessage("ReSharper", "UnusedMember.Global")] 
    public int PingClientCount => _pingProxyPool.ClientCount;
    public int UdpClientCount => _udpProxyPool.ClientCount;

    public int TcpConnectionCount { get { lock (_channels) return _channels.Count(x => x is not IDatagramChannel); } }
    public abstract Task OnPacketReceived(IPPacket ipPacket);
    public virtual void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint) { }
    public virtual void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, bool isNewLocalEndPoint, bool isNewRemoteEndPoint) { }
    protected abstract bool IsPingSupported { get; }

    protected ProxyManager(ISocketFactory socketFactory, ProxyManagerOptions options)
    {
        _pingProxyPool = new PingProxyPool(this, options.IcmpTimeout, options.MaxIcmpClientCount,  logScope: options.LogScope);
        _udpProxyPool = options.UseUdpProxy2
            ? new UdpProxyPoolEx(this, socketFactory, options.UdpTimeout, options.MaxUdpClientCount, logScope: options.LogScope)
            : new UdpProxyPool(this, socketFactory, options.UdpTimeout, options.MaxUdpClientCount, logScope: options.LogScope);
    }

    public async Task SendPackets(IList<IPPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++)
        {
            var ipPacket = ipPackets[i];
            await SendPacket(ipPacket).VhConfigureAwait();
        }
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
                    await _udpProxyPool.SendPacket(ipPacket).VhConfigureAwait();
                    break;

                case ProtocolType.Icmp or ProtocolType.IcmpV6:
                    if (!IsPingSupported)
                        throw new NotSupportedException("Ping is not supported by this proxy.");

                    await _pingProxyPool.SendPacket(ipPacket).VhConfigureAwait();
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

        lock (_channels)
            _channels.Add(channel);
        channel.Start();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _udpProxyPool.Dispose();
        _pingProxyPool.Dispose();

        // dispose channels
        var disposeTasks = new List<Task>();
        lock (_channels)
            disposeTasks.AddRange(_channels.Select(channel => channel.DisposeAsync(false).AsTask()));

        await Task.WhenAll(disposeTasks).VhConfigureAwait();
    }
}