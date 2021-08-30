using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Tunneling;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_Tunnel
    {
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
            clientUdpChannel.OnPacketReceived += delegate(object? sender, ChannelPacketReceivedEventArgs e)
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
            if (serverUdpClient.Client?.LocalEndPoint == null)
                throw new Exception($"{nameof(serverUdpClient)} connection has not been established!");
            clientUdpClient.Connect((IPEndPoint) serverUdpClient.Client.LocalEndPoint);
            UdpChannel clientUdpChannel = new(true, clientUdpClient, 200, aes.Key);

            Tunnel clientTunnel = new();
            clientTunnel.AddChannel(clientUdpChannel);
            clientTunnel.OnPacketReceived += delegate(object? sender, ChannelPacketReceivedEventArgs e)
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