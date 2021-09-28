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

namespace VpnHood.Test.Tests
{

    [TestClass]
    public class TunnelTest
    {
        [TestMethod]
        public async Task PingProxy_Pool()
        {
            // create icmp
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

            using var pingProxyPool = new PingProxyPool(2);
            var task1 = pingProxyPool.Send(PacketUtil.ClonePacket(ipPacket));

            icmpPacket.Sequence++;
            PacketUtil.UpdateIpPacket(ipPacket);
            var task2 = pingProxyPool.Send(PacketUtil.ClonePacket(ipPacket));

            icmpPacket.Sequence++;
            PacketUtil.UpdateIpPacket(ipPacket);

            var task3 = pingProxyPool.Send(PacketUtil.ClonePacket(ipPacket));
            
            await Task.WhenAll(task2, task3);
            Assert.AreEqual(TaskStatus.Faulted, task1.Status);
            Assert.AreEqual(TaskStatus.RanToCompletion, task2.Status);
            Assert.AreEqual(TaskStatus.RanToCompletion, task3.Status);

            // let reuse
            await pingProxyPool.Send(PacketUtil.ClonePacket(ipPacket));
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
            serverUdpChannel.OnPacketReceived += delegate(object? sender, ChannelPacketReceivedEventArgs e)
            {
                serverReceivedPackets = e.IpPackets.ToArray();
                _ = serverUdpChannel.SendPacketAsync(e.IpPackets);
            };

            // Create client
            var clientUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            if (serverUdpClient.Client.LocalEndPoint == null)
                throw new Exception("Client connection has not been established!");
            clientUdpClient.Connect((IPEndPoint) serverUdpClient.Client.LocalEndPoint);
            UdpChannel clientUdpChannel = new(true, clientUdpClient, 200, aes.Key);
            clientUdpChannel.Start();

            var clientReceivedPackets = Array.Empty<IPPacket>();
            clientUdpChannel.OnPacketReceived += delegate(object? _, ChannelPacketReceivedEventArgs e)
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
        public void UdpChannel_via_Tunnel()
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

            using var aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();

            // Create server
            var serverReceivedPackets = Array.Empty<IPPacket>();
            var serverUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            UdpChannel serverUdpChannel = new(false, serverUdpClient, 200, aes.Key);

            Tunnel serverTunnel = new();
            serverTunnel.AddChannel(serverUdpChannel);
            serverTunnel.OnPacketReceived += delegate(object? sender, ChannelPacketReceivedEventArgs e)
            {
                serverReceivedPackets = e.IpPackets.ToArray();
                _ = serverUdpChannel.SendPacketAsync(e.IpPackets);
            };

            // Create client
            var clientReceivedPackets = Array.Empty<IPPacket>();
            var clientUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            if (serverUdpClient.Client.LocalEndPoint == null)
                throw new Exception($"{nameof(serverUdpClient)} connection has not been established!");
            clientUdpClient.Connect((IPEndPoint) serverUdpClient.Client.LocalEndPoint);
            UdpChannel clientUdpChannel = new(true, clientUdpClient, 200, aes.Key);

            Tunnel clientTunnel = new();
            clientTunnel.AddChannel(clientUdpChannel);
            clientTunnel.OnPacketReceived += delegate(object? _, ChannelPacketReceivedEventArgs e)
            {
                clientReceivedPackets = e.IpPackets.ToArray();
                waitHandle.Set();
            };

            // send packet to server through tunnel
            clientTunnel.SendPacket(packets.ToArray());
            waitHandle.WaitOne(5000);
            Assert.AreEqual(packets.Count, serverReceivedPackets.Length);
            Assert.AreEqual(packets.Count, clientReceivedPackets.Length);
        }
    }
}