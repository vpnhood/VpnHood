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
        private static readonly HashSet<int> _tcpProctected = new();
        private static readonly HashSet<int> _udpProctected = new();

        private static readonly object _lockObject = new();

        public static int ServerPingTtl => 140;

        public static void ProtectSocket(Socket socket)
        {
            var localEndPoint = (IPEndPoint?) socket.LocalEndPoint ?? throw new Exception("Socket is not connected!");
            if (socket.ProtocolType == ProtocolType.Tcp) _tcpProctected.Add(localEndPoint.Port);
            else if (socket.ProtocolType == ProtocolType.Udp) _udpProctected.Add(localEndPoint.Port);
        }

        public static bool IsProtectedSocket(Socket socket)
        {
            var localEndPoint = (IPEndPoint?) socket.LocalEndPoint ?? throw new Exception("Socket is not connected!");
            if (socket.ProtocolType == ProtocolType.Tcp) _tcpProctected.Contains(localEndPoint.Port);
            else if (socket.ProtocolType == ProtocolType.Udp) _udpProctected.Contains(localEndPoint.Port);
            return false;
        }

        public static bool IsProtectedPacket(IPPacket ipPacket)
        {
            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Tcp)
            {
                var tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                return _tcpProctected.Contains(tcpPacket.SourcePort);
            }

            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Udp)
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                return _udpProctected.Contains(udpPacket.SourcePort);
            }

            // let server outbound call, go out: Icmp

            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
                //var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
                return ipPacket.TimeToLive == ServerPingTtl - 1;

            return false;
        }

        public static TcpClient CreateTcpClient(bool protect)
        {
            lock (_lockObject)
            {
                for (var i = _freeTcpPort; i <= 0xFFFF; i++)
                    try
                    {
                        var localEndPoint = new IPEndPoint(IPAddress.Any, i);
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

        public static UdpClient CreateUdpClient(bool protect)
        {
            lock (_lockObject)
            {
                for (var i = _freeUdpPort; i <= 0xFFFF; i++)
                    try
                    {
                        var localEndPoint = new IPEndPoint(IPAddress.Any, i);
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