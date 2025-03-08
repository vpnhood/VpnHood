using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using ProtocolType = PacketDotNet.ProtocolType;

// ReSharper disable UnusedMember.Global
namespace VpnHood.Core.Tunneling.Utils;

public static class PacketLogger
{
    public static void LogPackets(IList<IPPacket> ipPackets, string operation)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            LogPacket(ipPacket, operation);
        }
    }

    public static void LogPacket(IPPacket ipPacket, string message, LogLevel logLevel = LogLevel.Trace,
        Exception? exception = null)
    {
        try {
            if (!VhLogger.IsDiagnoseMode) return;
            var eventId = GeneralEventId.Packet;
            var packetPayload = Array.Empty<byte>();

            switch (ipPacket.Protocol) {
                case ProtocolType.Icmp: {
                        eventId = GeneralEventId.Ping;
                        var icmpPacket = ipPacket.ExtractIcmp();
                        packetPayload = icmpPacket.PayloadData ?? [];
                        break;
                    }

                case ProtocolType.IcmpV6: {
                        eventId = GeneralEventId.Ping;
                        var icmpPacket = ipPacket.ExtractIcmpV6();
                        packetPayload = icmpPacket.PayloadData ?? [];
                        break;
                    }

                case ProtocolType.Udp: {
                        eventId = GeneralEventId.Udp;
                        var udpPacket = ipPacket.ExtractUdp();
                        packetPayload = udpPacket.PayloadData ?? [];
                        break;
                    }

                case ProtocolType.Tcp: {
                        eventId = GeneralEventId.Tcp;
                        var tcpPacket = ipPacket.ExtractTcp();
                        packetPayload = tcpPacket.PayloadData ?? [];
                        break;
                    }
            }

            VhLogger.Instance.Log(logLevel, eventId, exception,
                message + " Packet: {Packet}, PayloadLength: {PayloadLength}, Payload: {Payload}",
                Format(ipPacket), packetPayload.Length,
                BitConverter.ToString(packetPayload, 0, Math.Min(10, packetPayload.Length)));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Packet,
                ex, "Could not extract packet for log. Packet: {Packet}, Message: {Message}, Exception: {Exception}",
                Format(ipPacket), message, exception);
        }
    }
    public static string Format(IPPacket ipPacket)
    {
        return VhLogger.FormatIpPacket(ipPacket.ToString()!);
    }
}