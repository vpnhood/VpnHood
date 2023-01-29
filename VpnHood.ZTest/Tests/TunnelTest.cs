using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.DatagramMessaging;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Test.Tests;

[TestClass]
public class TunnelTest
{
    private class PacketProxyReceiverTest : IPacketProxyReceiver
    {
        public int ReceivedCount { get; private set; }

        public Task OnPacketReceived(IPPacket packet)
        {
            lock(this)
                ReceivedCount++;
            return Task.CompletedTask;
        }

        public void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint)
        {
        }

        public void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
        }
    }

    [TestMethod]
    public async Task PingProxy_Pool()
    {
        // create icmp
        var packetReceiver = new PacketProxyReceiverTest();
        var payload = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var buffer = new byte[4 + payload.Length];
        var icmpPacket = new IcmpV4Packet(new ByteArraySegment(buffer))
        {
            TypeCode = IcmpV4TypeCode.EchoRequest,
            Id = 1,
            Sequence = 1,
            PayloadData = payload
        };


        var ipPacket = PacketUtil.CreateIpPacket(IPAddress.Loopback, IPAddress.Parse("8.8.8.8"));
        ipPacket.PayloadPacket = icmpPacket;
        PacketUtil.UpdateIpPacket(ipPacket);

        using var pingProxyPool = new PingProxyPool(packetReceiver, maxWorkerCount: 3);
        var task1 = pingProxyPool.SendPacket(PacketUtil.ClonePacket(ipPacket));

        ipPacket = PacketUtil.CreateIpPacket(IPAddress.Parse("127.0.0.1"), IPAddress.Parse("127.0.0.2"));
        ipPacket.PayloadPacket = icmpPacket;
        icmpPacket.Sequence++;
        PacketUtil.UpdateIpPacket(ipPacket);
        var task2 = pingProxyPool.SendPacket(PacketUtil.ClonePacket(ipPacket));

        ipPacket = PacketUtil.CreateIpPacket(IPAddress.Parse("127.0.0.1"), IPAddress.Parse("127.0.0.2"));
        ipPacket.PayloadPacket = icmpPacket;
        icmpPacket.Sequence++;
        PacketUtil.UpdateIpPacket(ipPacket);
        var task3 = pingProxyPool.SendPacket(PacketUtil.ClonePacket(ipPacket));

        await Task.WhenAll(task1, task2, task3);
        Assert.AreEqual(3, packetReceiver.ReceivedCount);

        // let reuse
        await pingProxyPool.SendPacket(PacketUtil.ClonePacket(ipPacket));
        Assert.AreEqual(4, packetReceiver.ReceivedCount);
    }

    [TestMethod]
    public void UdpChannel_Direct()
    {
        EventWaitHandle waitHandle = new(true, EventResetMode.AutoReset);
        waitHandle.Reset();

        // test packets
        var packets = new List<IPPacket>
        {
            IPPacket.RandomPacket(IPVersion.IPv4),
            IPPacket.RandomPacket(IPVersion.IPv4),
            IPPacket.RandomPacket(IPVersion.IPv4)
        };

        // Create server
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.GenerateKey();

        var serverUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        UdpChannel serverUdpChannel = new(false, serverUdpClient, 200, aes.Key);
        serverUdpChannel.Start();

        var serverReceivedPackets = Array.Empty<IPPacket>();
        serverUdpChannel.OnPacketReceived += delegate (object? sender, ChannelPacketReceivedEventArgs e)
        {
            serverReceivedPackets = e.IpPackets.ToArray();
            _ = serverUdpChannel.SendPacketAsync(e.IpPackets);
        };

        // Create client
        var clientUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        if (serverUdpClient.Client.LocalEndPoint == null)
            throw new Exception("Client connection has not been established!");
        clientUdpClient.Connect((IPEndPoint)serverUdpClient.Client.LocalEndPoint);
        UdpChannel clientUdpChannel = new(true, clientUdpClient, 200, aes.Key);
        clientUdpChannel.Start();

        var clientReceivedPackets = Array.Empty<IPPacket>();
        clientUdpChannel.OnPacketReceived += delegate (object? _, ChannelPacketReceivedEventArgs e)
        {
            clientReceivedPackets = e.IpPackets.ToArray();
            waitHandle.Set();
        };

        // send packet to server through channel
        _ = clientUdpChannel.SendPacketAsync(packets.ToArray());
        waitHandle.WaitOne(5000);
        Assert.AreEqual(packets.Count, serverReceivedPackets.Length);
        Assert.AreEqual(packets.Count, clientReceivedPackets.Length);
    }

    [TestMethod]
    public async Task UdpChannel_via_Tunnel()
    {
        var waitHandle = new EventWaitHandle(true, EventResetMode.AutoReset);
        waitHandle.Reset();

        // test packets
        var packets = new List<IPPacket>
        {
            IPPacket.RandomPacket(IPVersion.IPv4),
            IPPacket.RandomPacket(IPVersion.IPv4),
            IPPacket.RandomPacket(IPVersion.IPv4)
        };

        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.GenerateKey();

        // Create server
        var serverReceivedPackets = Array.Empty<IPPacket>();
        var serverUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverUdpChannel = new UdpChannel(false, serverUdpClient, 200, aes.Key);

        var serverTunnel = new Tunnel(new TunnelOptions());
        serverTunnel.AddChannel(serverUdpChannel);
        serverTunnel.OnPacketReceived += delegate (object? sender, ChannelPacketReceivedEventArgs e)
        {
            serverReceivedPackets = e.IpPackets.ToArray();
            _ = serverUdpChannel.SendPacketAsync(e.IpPackets);
        };

        // Create client
        var clientReceivedPackets = Array.Empty<IPPacket>();
        var clientUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        if (serverUdpClient.Client.LocalEndPoint == null)
            throw new Exception($"{nameof(serverUdpClient)} connection has not been established!");
        clientUdpClient.Connect((IPEndPoint)serverUdpClient.Client.LocalEndPoint);
        var clientUdpChannel = new UdpChannel(true, clientUdpClient, 200, aes.Key);

        var clientTunnel = new Tunnel();
        clientTunnel.AddChannel(clientUdpChannel);
        clientTunnel.OnPacketReceived += delegate (object? _, ChannelPacketReceivedEventArgs e)
        {
            clientReceivedPackets = e.IpPackets.ToArray();
            waitHandle.Set();
        };

        // send packet to server through tunnel
        await clientTunnel.SendPacket(packets.ToArray());
        await Task.Delay(5000);
        Assert.AreEqual(packets.Count, serverReceivedPackets.Length);
        Assert.AreEqual(packets.Count, clientReceivedPackets.Length);
    }

    [TestMethod]
    public void DatagramMessages()
    {
        var ipPacket = DatagramMessageHandler.CreateMessage(new CloseDatagramMessage());
        Assert.IsTrue(DatagramMessageHandler.IsDatagramMessage(ipPacket));
        
        var message = DatagramMessageHandler.ReadMessage(ipPacket);
        Assert.IsTrue(message is CloseDatagramMessage);
    }

}