using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using VpnHood.Tunneling;

namespace VpnHood.Test
{
    static class TestNetProtector
    {
        private static int _freeTcpPort = 13000;
        private static int _freeUdpPort = 13000;
        private static readonly HashSet<int> _tcpProctected = new();
        private static readonly HashSet<int> _udpProctected = new();

        public static int ServerPingTtl => 140;
        public static void ProtectSocket(Socket socket)
        {
            var localEndPoint = (IPEndPoint)socket.LocalEndPoint;
            if (socket.ProtocolType == System.Net.Sockets.ProtocolType.Tcp) _tcpProctected.Add(localEndPoint.Port);
            else if (socket.ProtocolType == System.Net.Sockets.ProtocolType.Udp) _udpProctected.Add(localEndPoint.Port);
        }

        public static bool IsProtectedSocket(Socket socket)
        {
            var localEndPoint = (IPEndPoint)socket.LocalEndPoint;
            if (socket.ProtocolType == System.Net.Sockets.ProtocolType.Tcp) _tcpProctected.Contains(localEndPoint.Port);
            else if (socket.ProtocolType == System.Net.Sockets.ProtocolType.Udp) _udpProctected.Contains(localEndPoint.Port);
            return false;
        }

        public static bool IsProtectedPacket(IPPacket ipPacket)
        {
            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Tcp)
            {
                var tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                return _tcpProctected.Contains(tcpPacket.SourcePort);
            }
            else if (ipPacket.Protocol == PacketDotNet.ProtocolType.Udp)
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                return _udpProctected.Contains(udpPacket.SourcePort);
            }

            // let server outbound call, go out: Icmp
            else if (ipPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
            {
                //var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
                return ipPacket.TimeToLive == (ServerPingTtl - 1);
            }

            return false;
        }

        private static readonly object _lockObject = new();
        public static TcpClient CreateTcpClient(bool protect)
        {
            lock (_lockObject)
                for (var i = _freeTcpPort; i <= 0xFFFF; i++)
                {
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
                for (var i = _freeUdpPort; i <= 0xFFFF; i++)
                {
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
