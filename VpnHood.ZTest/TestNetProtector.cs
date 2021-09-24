using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Tunneling;
using ProtocolType = System.Net.Sockets.ProtocolType;

namespace VpnHood.Test
{
    internal static class TestNetProtector
    {
        private static int _freeTcpPort = 13000;
        private static int _freeUdpPort = 13000;
        private static readonly HashSet<int> TcpProtected = new();
        private static readonly HashSet<int> UdpProtected = new();
        private static readonly object LockObject = new();

        public static int ServerPingTtl => 140;

        public static void ProtectSocket(Socket socket)
        {
            var localEndPoint = (IPEndPoint?) socket.LocalEndPoint ?? throw new Exception("Socket is not connected!");
            if (socket.ProtocolType == ProtocolType.Tcp) TcpProtected.Add(localEndPoint.Port);
            else if (socket.ProtocolType == ProtocolType.Udp) UdpProtected.Add(localEndPoint.Port);
        }

        // ReSharper disable once UnusedMember.Global
        public static bool IsProtectedSocket(Socket socket)
        {
            var localEndPoint = (IPEndPoint?) socket.LocalEndPoint ?? throw new Exception("Socket is not connected!");
            return socket.ProtocolType switch
            {
                ProtocolType.Tcp => TcpProtected.Contains(localEndPoint.Port),
                ProtocolType.Udp => UdpProtected.Contains(localEndPoint.Port),
                _ => false
            };
        }

        public static bool IsProtectedPacket(IPPacket ipPacket)
        {
            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Tcp)
            {
                var tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                return TcpProtected.Contains(tcpPacket.SourcePort);
            }

            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Udp)
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                return UdpProtected.Contains(udpPacket.SourcePort);
            }

            // let server outbound call, go out: Icmp
            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
                //var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
                return ipPacket.TimeToLive == ServerPingTtl - 1;

            return false;
        }

        public static TcpClient CreateTcpClient(AddressFamily addressFamily, bool protect)
        {
            lock (LockObject)
            {
                for (var i = _freeTcpPort; i <= 0xFFFF; i++)
                    try
                    {
                        var localEndPoint = new IPEndPoint(addressFamily ==AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, i);
                        var tcpClient = new TcpClient(localEndPoint);

                        if (protect)
                            ProtectSocket(tcpClient.Client);

                        _freeTcpPort = i + 1;
                        return tcpClient;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        // try next
                    }
            }

            throw new Exception("Could not find free port for test!");
        }

        public static UdpClient CreateUdpClient(AddressFamily addressFamily, bool protect)
        {
            lock (LockObject)
            {
                for (var i = _freeUdpPort; i <= 0xFFFF; i++)
                    try
                    {
                        var localEndPoint = new IPEndPoint(addressFamily ==AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, i);
                        var udpClient = new UdpClient(localEndPoint);

                        if (protect)
                            ProtectSocket(udpClient.Client);

                        _freeUdpPort = i + 1;
                        return udpClient;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        // try next
                    }
            }

            throw new Exception("Could not find free port for test!");
        }
    }
}