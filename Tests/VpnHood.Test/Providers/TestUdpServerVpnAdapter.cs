using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Test.Providers;

public class TestUdpServerVpnAdapter : PacketTransport, IVpnAdapter, IPacketProxyCallbacks
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly UdpProxyPool _proxyPool;
    public IPAddress? VirtualIp { get; set; }

    public TestUdpServerVpnAdapter()
        : base(new PacketTransportOptions {
            Blocking = false,
            AutoDisposePackets = true
        })
    {
        _proxyPool = new UdpProxyPool(new UdpProxyPoolOptions {
            SocketFactory = new SocketFactory(),
            PacketProxyCallbacks = this,
            UdpTimeout = TunnelDefaults.UdpTimeout,
            MaxClientCount = TunnelDefaults.MaxUdpClientCount,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            SendBufferSize = null,
            ReceiveBufferSize = null,
            AutoDisposePackets = true,
            LogScope = null
        });
        _proxyPool.PacketReceived += Proxy_PacketReceived;
    }

    public event EventHandler? Disposed;
    public bool IsStarted { get; private set; }
    public bool IsNatSupported => true;
    public bool CanProtectSocket => false;
    public bool ProtectSocket(Socket socket) => false;
    public bool ProtectSocket(Socket socket, IPAddress ipAddress) => false;
    public Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        IsStarted = true;
        return Task.CompletedTask;
    }

    public void Stop()
    {
        IsStarted = false;
    }

    protected override ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            SendPacket(ipPacket);

        return default;
    }

    public void SendPacket(IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Udp) {
            VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestTunProvider: Not Udp packet.");
            return;
        }

        ipPacket = ipPacket.Clone(); // caller will dispose the packet
        _proxyPool.SendPacketQueued(ipPacket);
    }

    public IPAddress GetPrimaryAdapterAddress(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? IPAddress.Loopback : IPAddress.IPv6Loopback;
    }

    public bool IsIpVersionSupported(IpVersion ipVersion) => true;

    private void Proxy_PacketReceived(object? sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    public void OnConnectionRequested(IpProtocol protocolType, IPEndPoint remoteEndPoint)
    {
    }

    public void OnConnectionEstablished(IpProtocol protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
        bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
    {
    }

    protected override void DisposeManaged()
    {
        _cancellationTokenSource.Dispose();
        _proxyPool.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);

        base.DisposeManaged();
    }
}