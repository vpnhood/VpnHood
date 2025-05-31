using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Exceptions;

// ReSharper disable UnusedMember.Global
namespace VpnHood.Core.Tunneling.Utils;

public static class PacketLogger
{
    public static void LogPackets(IList<IpPacket> ipPackets, string operation)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            LogPacket(ipPacket, operation);
        }
    }

    public static void LogPacket(IpPacket ipPacket, string message, LogLevel logLevel = LogLevel.Trace,
        Exception? exception = null, EventId? eventId = null)
    {
        try {
            if (VhLogger.MinLogLevel > LogLevel.Trace) 
                return;

            var packetEventId = GeneralEventId.Packet;
            var packetPayload = new Memory<byte>();

            switch (ipPacket.Protocol) {
                case IpProtocol.IcmpV4: {
                        packetEventId = GeneralEventId.Ping;
                        var icmpPacket = ipPacket.ExtractIcmpV4();
                        packetPayload = icmpPacket.Payload;
                        break;
                    }

                case IpProtocol.IcmpV6: {
                        packetEventId = GeneralEventId.Ping;
                        var icmpPacket = ipPacket.ExtractIcmpV6();
                        packetPayload = icmpPacket.Payload;
                        break;
                    }

                case IpProtocol.Udp: {
                        packetEventId = GeneralEventId.Udp;
                        var udpPacket = ipPacket.ExtractUdp();
                        packetPayload = udpPacket.Payload;
                        break;
                    }

                case IpProtocol.Tcp: {
                        packetEventId = GeneralEventId.Tcp;
                        var tcpPacket = ipPacket.ExtractTcp();
                        packetPayload = tcpPacket.Payload;
                        break;
                    }
            }

            // set network filter event id if the exception is NetFilterException
            if (exception is NetFilterException)
                eventId = GeneralEventId.NetFilter;

            // Log the packet
            VhLogger.Instance.Log(logLevel, eventId ?? packetEventId, exception,
                message + " Packet: {Packet}, PayloadLength: {PayloadLength}, Payload: {Payload}",
                Format(ipPacket), packetPayload.Length,
                BitConverter.ToString(packetPayload.ToArray(), 0, Math.Min(10, packetPayload.Length)));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Packet,
                ex, "Could not extract packet for log. Packet: {Packet}, Message: {Message}, Exception: {Exception}",
                Format(ipPacket), message, exception);
        }
    }
    public static string Format(IpPacket ipPacket)
    {
        return VhLogger.FormatIpPacket(ipPacket.ToString());
    }
}