using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Test.Providers;

public class TestUdpServerVpnAdapter : IVpnAdapter, IPacketProxyReceiver
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    private readonly UdpProxyPool _proxyPool;
    public IPAddress? VirtualIp { get; set; }

    public TestUdpServerVpnAdapter()
    {
        _proxyPool = new UdpProxyPool(this, new SocketFactory(), udpTimeout: null, maxClientCount: null);
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

    public void SendPacket(IPPacket ipPacket)
    {
        if (ipPacket.Protocol != ProtocolType.Udp) {
            VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestTunProvider: Not Udp packet.");
            return;
        }

        _proxyPool.SendPacket(ipPacket).GetAwaiter().GetResult();
    }

    public void SendPackets(IList<IPPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets) {
            _proxyPool.SendPacket(ipPacket).GetAwaiter().GetResult();
        }
    }

    Task IPacketReceiver.OnPacketReceived(IPPacket ipPacket)
    {
        PacketReceived?.Invoke(this, new PacketReceivedEventArgs([ipPacket]));
        return Task.CompletedTask;
    }

    public void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint)
    {
    }

    public void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
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