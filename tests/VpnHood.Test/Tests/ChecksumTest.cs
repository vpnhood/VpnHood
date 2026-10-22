using System.Buffers.Binary;
using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class ChecksumTest : TestBase
{
    // RFC 1071 as written: the pseudo header and the data as one stream of network-order words, the carry added
    // back at each step
    private static ushort WordByWordChecksum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        var stream = new byte[pseudoHeader.Length + data.Length];
        pseudoHeader.CopyTo(stream);
        data.CopyTo(stream.AsSpan(pseudoHeader.Length));

        uint sum = 0;
        for (var i = 0; i < stream.Length; i += 2) {
            sum += (uint)((stream[i] << 8) | (i + 1 < stream.Length ? stream[i + 1] : 0));
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    private static IPAddress Address(IpVersion ipVersion, byte last)
    {
        return IPAddress.Parse(ipVersion == IpVersion.IPv4 ? $"10.1.2.{last}" : $"2001:db8::{last}");
    }

    // The loop this machine runs (Vector256 on x64, AdvSimd on Arm64) and every tail, from aligned and unaligned starts,
    // on zeros, all ones and random bytes, after pseudo headers of even and odd length; under DOTNET_EnableAVX2=0 an
    // x64 run takes the scalar loop, Mono's
    [TestMethod]
    public void Sum_matches_a_word_by_word_sum()
    {
        var random = new Random(1);
        var buffer = new byte[2200];
        foreach (var pseudoHeaderLength in new[] { 0, 1, 3, 12, 40 }) {
            var pseudoHeader = new byte[pseudoHeaderLength];
            random.NextBytes(pseudoHeader);
            for (var fill = 0; fill < 3; fill++) {
                if (fill == 2)
                    random.NextBytes(buffer);
                else
                    Array.Fill(buffer, fill == 0 ? (byte)0 : (byte)0xFF);

                for (var length = 0; length <= 2100; length++) {
                    foreach (var start in new[] { 0, 1, 3 }) {
                        var data = buffer.AsSpan(start, length);
                        var expected = WordByWordChecksum(pseudoHeader, data);
                        var actual = ChecksumUtils.OnesComplementSum(pseudoHeader, data);
                        if (actual != expected)
                            Assert.Fail($"PseudoHeader: {pseudoHeaderLength}, Fill: {fill}, Length: {length}, " +
                                        $"Start: {start}, Actual: {actual:X4}, Expected: {expected:X4}");
                    }
                }
            }
        }
    }

    // The checksum written into the data adds its own complement, so the sum becomes all ones and the next
    // checksum is zero: TCP and ICMP carry it as it is, and a field of either zero passes the check
    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void Tcp_carries_a_computed_zero(IpVersion ipVersion)
    {
        using var ipPacket = PacketBuilder.BuildTcp(Address(ipVersion, 1), Address(ipVersion, 2), 1000, 443, [],
            new byte[11]);
        var tcpPacket = ipPacket.ExtractTcp();
        BinaryPrimitives.WriteUInt16BigEndian(tcpPacket.Payload.Span, tcpPacket.ComputeChecksum(ipPacket));
        tcpPacket.UpdateChecksum(ipPacket);
        Assert.AreEqual(0, tcpPacket.Checksum);
        Assert.IsTrue(tcpPacket.IsChecksumValid(ipPacket));

        tcpPacket.Checksum = 0xFFFF;
        Assert.IsTrue(tcpPacket.IsChecksumValid(ipPacket));

        tcpPacket.Checksum = 1;
        Assert.IsFalse(tcpPacket.IsChecksumValid(ipPacket));
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void Icmp_carries_a_computed_zero(IpVersion ipVersion)
    {
        using var ipPacket = PacketBuilder.BuildIcmpEchoRequest(Address(ipVersion, 1), Address(ipVersion, 2),
            new byte[9]);
        var icmpPacket = ipVersion == IpVersion.IPv4
            ? (IChecksumPayloadPacket)ipPacket.ExtractIcmpV4()
            : ipPacket.ExtractIcmpV6();
        BinaryPrimitives.WriteUInt16BigEndian(icmpPacket.Buffer.Span[8..], icmpPacket.ComputeChecksum(ipPacket));
        icmpPacket.UpdateChecksum(ipPacket);
        Assert.AreEqual(0, icmpPacket.Checksum);
        Assert.IsTrue(icmpPacket.IsChecksumValid(ipPacket));
    }

    [TestMethod]
    public void IpV4_header_carries_a_computed_zero()
    {
        using var ipPacket = (IpV4Packet)PacketBuilder.BuildIp(Address(IpVersion.IPv4, 1), Address(IpVersion.IPv4, 2),
            IpProtocol.Raw, 0);
        ipPacket.UpdateHeaderChecksum();
        ipPacket.Identification = ipPacket.HeaderChecksum;
        ipPacket.UpdateHeaderChecksum();
        Assert.AreEqual(0, ipPacket.HeaderChecksum);
        Assert.IsTrue(ipPacket.IsHeaderChecksumValid());

        ipPacket.HeaderChecksum = 0xFFFF;
        Assert.IsTrue(ipPacket.IsHeaderChecksumValid());

        ipPacket.HeaderChecksum = 1;
        Assert.IsFalse(ipPacket.IsHeaderChecksumValid());
    }

    // a zero UDP field says there is no checksum (RFC 768), so a computed zero goes as all ones
    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void Udp_sends_a_computed_zero_as_all_ones(IpVersion ipVersion)
    {
        using var ipPacket = PacketBuilder.BuildUdp(Address(ipVersion, 1), Address(ipVersion, 2), 1000, 53,
            new byte[11]);
        var udpPacket = ipPacket.ExtractUdp();
        BinaryPrimitives.WriteUInt16BigEndian(udpPacket.Payload.Span, udpPacket.ComputeChecksum(ipPacket));
        udpPacket.UpdateChecksum(ipPacket);
        Assert.AreEqual(0xFFFF, udpPacket.Checksum);
        Assert.IsTrue(udpPacket.IsChecksumValid(ipPacket));
    }

    // RFC 8200 8.1: IPv6 has no UDP without a checksum
    [TestMethod]
    [DataRow(IpVersion.IPv4, true)]
    [DataRow(IpVersion.IPv6, false)]
    public void Udp_without_checksum_is_valid_over_IPv4_only(IpVersion ipVersion, bool isValid)
    {
        using var ipPacket = PacketBuilder.BuildUdp(Address(ipVersion, 1), Address(ipVersion, 2), 1000, 53,
            new byte[11]);
        var udpPacket = ipPacket.ExtractUdp();
        udpPacket.Checksum = 0;
        Assert.AreEqual(isValid, udpPacket.IsChecksumValid(ipPacket));
    }

    [TestMethod]
    public void Udp_without_checksum_needs_IPv4_on_both_sides()
    {
        using var ipPacket = PacketBuilder.BuildUdp(Address(IpVersion.IPv4, 1), Address(IpVersion.IPv4, 2), 1000, 53,
            new byte[11]);
        var udpPacket = ipPacket.ExtractUdp();
        udpPacket.Checksum = 0;
        Assert.IsFalse(udpPacket.IsChecksumValid(ipPacket.SourceAddressSpan,
            Address(IpVersion.IPv6, 2).GetAddressBytes()));
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4, IpProtocol.Tcp)]
    [DataRow(IpVersion.IPv6, IpProtocol.Tcp)]
    [DataRow(IpVersion.IPv4, IpProtocol.Udp)]
    [DataRow(IpVersion.IPv6, IpProtocol.Udp)]
    [DataRow(IpVersion.IPv4, IpProtocol.IcmpV4)]
    [DataRow(IpVersion.IPv6, IpProtocol.IcmpV6)]
    public void A_changed_byte_fails_the_check(IpVersion ipVersion, IpProtocol protocol)
    {
        var payload = new byte[1400];
        new Random(2).NextBytes(payload);
        using var ipPacket = protocol switch {
            IpProtocol.Tcp => PacketBuilder.BuildTcp(Address(ipVersion, 1), Address(ipVersion, 2), 1000, 443, [], payload),
            IpProtocol.Udp => PacketBuilder.BuildUdp(Address(ipVersion, 1), Address(ipVersion, 2), 1000, 443, payload),
            _ => PacketBuilder.BuildIcmpEchoRequest(Address(ipVersion, 1), Address(ipVersion, 2), payload)
        };
        ipPacket.UpdateAllChecksums();
        IChecksumPayloadPacket payloadPacket = protocol switch {
            IpProtocol.Tcp => ipPacket.ExtractTcp(),
            IpProtocol.Udp => ipPacket.ExtractUdp(),
            IpProtocol.IcmpV4 => ipPacket.ExtractIcmpV4(),
            _ => ipPacket.ExtractIcmpV6()
        };
        Assert.IsTrue(payloadPacket.IsChecksumValid(ipPacket));
        Assert.IsTrue(ipPacket is not IpV4Packet ipV4Packet || ipV4Packet.IsHeaderChecksumValid());

        payloadPacket.Buffer.Span[^100] ^= 0x10;
        Assert.IsFalse(payloadPacket.IsChecksumValid(ipPacket));

        if (ipPacket is IpV4Packet changedIpV4Packet) {
            changedIpV4Packet.TimeToLive--;
            Assert.IsFalse(changedIpV4Packet.IsHeaderChecksumValid());
        }
    }
}
