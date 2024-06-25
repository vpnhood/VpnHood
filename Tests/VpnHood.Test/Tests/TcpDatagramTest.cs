using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Channels;
using VpnHood.Tunneling.ClientStreams;
using VpnHood.Tunneling.DatagramMessaging;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class TcpDatagramChannelTest : TestBase
{
    [TestMethod]
    public void DatagramMessages()
    {
        var ipPacket = DatagramMessageHandler.CreateMessage(new CloseDatagramMessage());
        Assert.IsTrue(DatagramMessageHandler.IsDatagramMessage(ipPacket));

        var message = DatagramMessageHandler.ReadMessage(ipPacket);
        Assert.IsTrue(message is CloseDatagramMessage);
    }

    [TestMethod]
    public async Task AutoCloseChannel()
    {
        // create server tcp listener
        var tcpEndPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        var tcpListener = new TcpListener(tcpEndPoint);
        tcpListener.Start();
        var listenerTask = tcpListener.AcceptTcpClientAsync();

        // create tcp client and connect
        using var tcpClient = new TcpClient();
        var connectTask = tcpClient.ConnectAsync(tcpEndPoint);
        await Task.WhenAll(listenerTask, connectTask);

        // create server channel
        var serverTcpClient = await listenerTask;
        await using var serverStream = new TcpClientStream(serverTcpClient, serverTcpClient.GetStream(), Guid.NewGuid() + ":server");
        await using var serverChannel = new StreamDatagramChannel(serverStream, Guid.NewGuid().ToString());
        
        var serverTunnel = new Tunnel(new TunnelOptions());
        serverTunnel.AddChannel(serverChannel);
        IPPacket? lastServerReceivedPacket = null;
        serverTunnel.PacketReceived += (_, args) =>
        {
            lastServerReceivedPacket = args.IpPackets.Last();
        };

        // create client channel
        await using var clientStream = new TcpClientStream(tcpClient, tcpClient.GetStream(), Guid.NewGuid() + ":client");
        await using var clientChannel = new StreamDatagramChannel(clientStream, Guid.NewGuid().ToString(), TimeSpan.FromMilliseconds(1000));
        await using var clientTunnel = new Tunnel(new TunnelOptions());
        clientTunnel.AddChannel(clientChannel);

        // -------
        // Check sending packet to server
        // ------
        var testPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("1.1.1.1:1"), IPEndPoint.Parse("1.1.1.1:2"), [1, 2, 3]);
        await clientTunnel.SendPacketsAsync([testPacket], CancellationToken.None);
        await VhTestUtil.AssertEqualsWait(testPacket.ToString(), () => lastServerReceivedPacket?.ToString());
        await VhTestUtil.AssertEqualsWait(0, () => clientTunnel.DatagramChannelCount);
        await VhTestUtil.AssertEqualsWait(0, () => serverTunnel.DatagramChannelCount);
    }


}