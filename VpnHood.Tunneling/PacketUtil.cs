using PacketDotNet;
using PacketDotNet.Utils;
using System;

namespace VpnHood.Tunneling
{
    public static class PacketUtil
    {
        public static IPPacket CreateUnreachableReply(IPPacket ipPacket, IcmpV4TypeCode typeCode, ushort sequence = 0)
        {
            // packet is too big
            var icmpDataLen = Math.Min(ipPacket.TotalLength, 20 + 8);
            var byteArraySegment = new ByteArraySegment(new byte[16 + icmpDataLen]);
            var icmpV4Packet = new IcmpV4Packet(byteArraySegment, ipPacket)
            {
                TypeCode = typeCode,
                PayloadData = ipPacket.Bytes[..icmpDataLen],
                Sequence = sequence
            };
            icmpV4Packet.Checksum = 0;
            icmpV4Packet.Checksum = (ushort)ChecksumUtils.OnesComplementSum(icmpV4Packet.Bytes, 0, icmpV4Packet.Bytes.Length);
            icmpV4Packet.UpdateCalculatedValues();

            var newIpPacket = new IPv4Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress)
            {
                PayloadPacket = icmpV4Packet,
                FragmentFlags = 0,
            };
            newIpPacket.UpdateIPChecksum();
            newIpPacket.UpdateCalculatedValues();
            return newIpPacket;
        }
        public static void UpdateICMPChecksum(IcmpV4Packet icmpPacket)
        {
            icmpPacket.Checksum = 0;
            var buf = icmpPacket.Bytes;
            icmpPacket.Checksum = (ushort)ChecksumUtils.OnesComplementSum(buf, 0, buf.Length);
        }
    }
}
