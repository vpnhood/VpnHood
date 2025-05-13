using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Test.Providers;

public class TestUdpServerVpnAdapter : IVpnAdapter, IPacketProxyCallbacks
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    private readonly UdpProxyPool _proxyPool;
    public IPAddress? VirtualIp { get; set; }

    public TestUdpServerVpnAdapter()
    {
        _proxyPool = new UdpProxyPool(new UdpProxyPoolOptions {
            SocketFactory = new SocketFactory(),
            PacketProxyCallbacks = this
        });
        _proxyPool.PacketReceived += Proxy_PacketReceived;
    }

    public event EventHandler? Disposed;
    public bool Started { get; private set; }
    public bool IsNatSupported => true;
    public bool CanProtectSocket => false;
    public bool ProtectSocket(Socket socket) => false;
    public bool ProtectSocket(Socket socket, IPAddress ipAddress) => false;
    public Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public void Stop()
    {
        Started = false;
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

    public void SendPackets(IList<IpPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets) {
            SendPacket(ipPacket);
        }
    }

    public IPAddress GetPrimaryAdapterAddress(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? IPAddress.Loopback : IPAddress.IPv6Loopback;
    }

    public bool IsIpVersionSupported(IpVersion ipVersion) => true;

    private void Proxy_PacketReceived(object? sender, PacketReceivedEventArgs e)
    {
        PacketReceived?.Invoke(this, e);
    }

    public void OnConnectionRequested(IpProtocol protocolType, IPEndPoint remoteEndPoint)
    {
    }

    public void OnConnectionEstablished(IpProtocol protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
        bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
    {
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
        _proxyPool.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}