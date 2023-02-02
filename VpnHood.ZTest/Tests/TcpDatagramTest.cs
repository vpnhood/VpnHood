using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.DatagramMessaging;

namespace VpnHood.Test.Tests;

[TestClass]
public class TcpDatagramChannelTest
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
        var tcpEndPoint = Util.GetFreeTcpEndPoint(IPAddress.Loopback);
        var tcpListener = new TcpListener(tcpEndPoint);
        tcpListener.Start();
        var listenerTask = tcpListener.AcceptTcpClientAsync();

        // create tcp client and connect
        using var tcpClient = new TcpClient();
        var connectTask = tcpClient.ConnectAsync(tcpEndPoint);
        await Task.WhenAll(listenerTask, connectTask);

        // create server channel
        var serverTcpClient = await listenerTask;
        var serverStream = new TcpClientStream(serverTcpClient, serverTcpClient.GetStream());
        using var serverChannel = new TcpDatagramChannel(serverStream);
        using var serverTunnel = new Tunnel(new TunnelOptions());
        serverTunnel.AddChannel(serverChannel);
        IPPacket? lastServerReceivedPacket = null;
        serverTunnel.OnPacketReceived += (_, args) =>
        {
            lastServerReceivedPacket = args.IpPackets.Last();
        };

        // create client channel
        var clientStream = new TcpClientStream(tcpClient, tcpClient.GetStream());
        using var clientChannel = new TcpDatagramChannel(clientStream, TimeSpan.FromMilliseconds(1000));
        using var clientTunnel = new Tunnel(new TunnelOptions());
        clientTunnel.AddChannel(clientChannel);

        // -------
        // Check sending packet to server
        // ------
        var testPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("1.1.1.1:1"), IPEndPoint.Parse("1.1.1.1:2"), new byte[] { 1, 2, 3 });
        await clientTunnel.SendPacket(testPacket);
        await TestHelper.AssertEqualsWait(testPacket.ToString(), () => lastServerReceivedPacket?.ToString());
        await TestHelper.AssertEqualsWait(0, () => clientTunnel.DatagramChannels.Length);
        await TestHelper.AssertEqualsWait(0, () => serverTunnel.DatagramChannels.Length);
    }


}