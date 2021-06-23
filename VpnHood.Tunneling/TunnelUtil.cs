using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Generic;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public static class TunnelUtil
    {
        public const int SocketStackSize_Datagram = 65536 * 2;
        public const int SocketStackSize_Stream = 65536 * 2;
        public const int TlsHandshakeLength = 5000;
        public const int MtuWithFragmentation = 0xFFFF - 70;
        public const int MtuWithoutFragmentation = 1500 - 70;

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

        public static void LogPackets(IEnumerable<IPPacket> ipPackets, string operation)
        {
            foreach (var ipPacket in ipPackets)
                LogPacket(ipPacket, operation);
        }

        public static void LogPacket(IPPacket ipPacket, string operation)
        {
            if (!VhLogger.IsDiagnoseMode) return;

            // log ICMP
            if (ipPacket.Protocol == ProtocolType.Icmp)
            {
                var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                if (icmpPacket != null)
                {
                    var payload = icmpPacket.PayloadData ?? Array.Empty<byte>();
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping, $"{ipPacket.Protocol} has been {operation}. DestAddress: {ipPacket.DestinationAddress}, DataLen: {payload.Length}, Data: {BitConverter.ToString(payload, 0, Math.Min(10, payload.Length))}.");
                }
                else
                {
                    VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Ping, $"Invalid {ipPacket.Protocol} packet has been {operation}! DestAddress: {ipPacket.DestinationAddress}, PacketLength: {ipPacket.TotalLength}.");
                }
            }

            // log Udp
            if (ipPacket.Protocol == ProtocolType.Udp)
            {
                var udpPacket = ipPacket.Extract<UdpPacket>();
                if (udpPacket != null)
                {
                    var payload = udpPacket.PayloadData ?? Array.Empty<byte>();
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"{ipPacket.Protocol} has been {operation}. DestAddress: {ipPacket.DestinationAddress}:{udpPacket.DestinationPort}, DataLen: {payload.Length}, Data: {BitConverter.ToString(payload, 0, Math.Min(10, payload.Length))}.");
                }
                else
                {
                    VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp, $"Invalid {ipPacket.Protocol} has been {operation}! DestAddress: {ipPacket.DestinationAddress}, PacketLength: {ipPacket.TotalLength}.");
                }
            }
        }

    }
}
