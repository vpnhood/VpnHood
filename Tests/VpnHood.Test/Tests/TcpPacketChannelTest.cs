using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.DatagramMessaging;

namespace VpnHood.Test.Tests;

[TestClass]
public class TcpPacketChannelTest : TestBase
{
    [TestMethod]
    public void DatagramMessages()
    {
        var ipPacket = PacketMessageHandler.CreateMessage(new ClosePacketMessage());
        Assert.IsTrue(PacketMessageHandler.IsPacketMessage(ipPacket));

        var message = PacketMessageHandler.ReadMessage(ipPacket);
        Assert.IsTrue(message is ClosePacketMessage);
    }

    [TestMethod]
    public async Task AutoCloseChannel()
    {
        // create server tcp listener
        var tcpEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var tcpListener = new TcpListener(tcpEndPoint);
        tcpListener.Start();
        var listenerTask = tcpListener.AcceptTcpClientAsync();

        // create tcp client and connect
        using var tcpClient = new TcpClient();
        var connectTask = tcpClient.ConnectAsync(tcpEndPoint);
        await Task.WhenAll(listenerTask, connectTask);

        // create server channel
        var serverTcpClient = await listenerTask;
        using var serverStream = new TcpClientStream(serverTcpClient, serverTcpClient.GetStream(), Guid.NewGuid() + ":server");
        using var serverChannel = new StreamPacketChannel(new StreamPacketChannelOptions {
            ClientStream = serverStream,
            Blocking = false,
            AutoDisposePackets = true,
            Lifespan = null,
            ChannelId = Guid.NewGuid().ToString(),
        });
  
        using var serverTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        serverTunnel.AddChannel(serverChannel);
        IpPacket? lastServerReceivedPacket = null;
        serverTunnel.PacketReceived += (_, ipPacket) => {
            lastServerReceivedPacket = ipPacket;
        };

        // create client channel
        using var clientStream = new TcpClientStream(tcpClient, tcpClient.GetStream(), Guid.NewGuid() + ":client:tunnel");
        using var clientChannel = new StreamPacketChannel(new StreamPacketChannelOptions {
            ClientStream = clientStream,
            AutoDisposePackets = true,
            Lifespan = TimeSpan.FromMilliseconds(1000),
            Blocking = false,
            ChannelId = Guid.NewGuid().ToString(),
        });

        using var clientTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        clientTunnel.AddChannel(clientChannel);

        // -------
        // Check sending packet to server
        // ------
        using var testPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("1.1.1.1:1"), IPEndPoint.Parse("1.1.1.1:2"), [1, 2, 3]);
        clientTunnel.SendPacketQueued(testPacket.Clone());
        await VhTestUtil.AssertEqualsWait(true, () => 
            lastServerReceivedPacket!=null && testPacket.Buffer.Span.SequenceEqual(lastServerReceivedPacket.Buffer.Span) );
        await VhTestUtil.AssertEqualsWait(0, () => clientTunnel.PacketChannelCount);
        await VhTestUtil.AssertEqualsWait(0, () => serverTunnel.PacketChannelCount);
    }
}