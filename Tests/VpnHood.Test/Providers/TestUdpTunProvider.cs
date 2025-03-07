using System.Net;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Test.Providers;

public class TestUdpTunProvider : ITunProvider, IPacketProxyReceiver
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    public event EventHandler<IPPacket>? OnPacketReceived;
    private readonly UdpProxyPool _proxyPool;
    public IPAddress? VirtualIp { get; set; }

    public TestUdpTunProvider()
    {
        _proxyPool = new UdpProxyPool(this, new SocketFactory(), udpTimeout: null, maxClientCount: null);
    }

    public async Task SendPacket(IPPacket ipPacket)
    {
        if (ipPacket.Protocol != ProtocolType.Udp) {
            VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestTunProvider: Not Udp packet.");
            return;
        }

        await _proxyPool.SendPacket(ipPacket);
    }

    Task IPacketReceiver.OnPacketReceived(IPPacket packet)
    {
        OnPacketReceived?.Invoke(this, packet);

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
    }
}