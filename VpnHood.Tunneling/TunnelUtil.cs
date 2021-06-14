using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using System;
using System.Linq;
using System.Net;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public static class TunnelUtil
    {
        public const int SocketStackSize_Datagram = 65536 * 2;
        public const int SocketStackSize_Stream = 65536 * 2;
        public const int TlsHandshakeLength = 5000;
        
        public static ulong RandomLong()
        {
            var random = new Random();
            byte[] bytes = new byte[8];
            random.NextBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        public static int RandomInt()
        {
            var random = new Random();
            return random.Next();
        }

        public static void LogPackets(IPPacket[] ipPackets, string operation)
        {
            foreach (var ipPacket in ipPackets)
                LogPacket(ipPacket, operation);
        }

        public static void LogPacket(IPPacket ipPacket, string operation)
        {
            // log ICMP
            if (VhLogger.IsDiagnoseMode && ipPacket.Protocol == ProtocolType.Icmp)
            {
                var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                var payload = icmpPacket.PayloadData ?? Array.Empty<byte>();
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping, $"ICMP has been {operation}. DestAddress: {ipPacket.DestinationAddress}, DataLen: {payload.Length}, Data: {BitConverter.ToString(payload, 0, Math.Min(10, payload.Length))}.");
            }

            // log Udp
            if (VhLogger.IsDiagnoseMode && ipPacket.Protocol == ProtocolType.Udp)
            {
                var udp = ipPacket.Extract<UdpPacket>();
                var payload = udp.PayloadData ?? Array.Empty<byte>();
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"UDP has been {operation}. DestAddress: {ipPacket.DestinationAddress}:{udp.DestinationPort}, DataLen: {payload.Length}, Data: {BitConverter.ToString(payload, 0, Math.Min(10, payload.Length))}.");
            }
        }

    }
}
