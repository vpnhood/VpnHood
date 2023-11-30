using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Tunneling;

namespace VpnHood.Test;

internal static class TestSocketProtector
{
    private const int ProtectedTtl = 111;
    private static readonly HashSet<int> TcpProtected = new();

    public static void ProtectSocket(Socket socket)
    {
        socket.Ttl = ProtectedTtl;
    }

    public static bool IsProtectedPacket(IPPacket ipPacket)
    {
        if (ipPacket.Protocol == PacketDotNet.ProtocolType.Tcp)
        {
            lock (TcpProtected)
            {
                var tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                if (tcpPacket.Synchronize)
                {
                    if (ipPacket.TimeToLive == ProtectedTtl)
                        TcpProtected.Add(tcpPacket.SourcePort);
                    else
                        TcpProtected.Remove(tcpPacket.SourcePort);
                }

                return TcpProtected.Contains(tcpPacket.SourcePort);
            }
        }

        return ipPacket.TimeToLive == ProtectedTtl;
    }
}