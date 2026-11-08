using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

// VpnHood.Net.Packets' README and its doc comments' examples are copies of the Snippet regions here, made by
// pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit a region, then run the script.
[TestClass]
public class PacketsReadmeExampleTest
{
    [TestMethod]
    public void Build_udp()
    {
        #region Snippet:PacketsBuildUdp
        using var ipPacket = PacketBuilder.BuildUdp(
            new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5353),
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 53),
            "hello"u8);

        ipPacket.UpdateAllChecksums(); // BuildUdp computes no checksum itself
        var bytes = ipPacket.Buffer; // the packet's own bytes, ready for a TUN device
        #endregion

        using var parsed = PacketBuilder.Parse(bytes.Span);
        Assert.IsTrue(((IpV4Packet)parsed).IsHeaderChecksumValid());
        Assert.IsTrue(parsed.ExtractUdp().IsChecksumValid(parsed));
        Assert.AreEqual(53, parsed.ExtractUdp().DestinationPort);
    }

    [TestMethod]
    public void Parse()
    {
        using var sample = PacketBuilder.BuildUdp(
            new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5353),
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 53),
            "hello"u8);
        sample.UpdateAllChecksums();
        var received = sample.Buffer.ToArray(); // as read from a TUN device or a raw socket

        #region Snippet:PacketsParse
        // Parse copies the received bytes into pooled memory; PacketBuilder.Attach would read them in place
        using var ipPacket = PacketBuilder.Parse(received);
        Console.WriteLine($"{ipPacket.SourceAddress} -> {ipPacket.DestinationAddress}, {ipPacket.Protocol}");

        if (ipPacket.Protocol == IpProtocol.Udp && !ipPacket.IsFragment) {
            var udp = ipPacket.ExtractUdp(); // a view over the packet's bytes, not a copy
            Console.WriteLine($"UDP {udp.SourcePort} -> {udp.DestinationPort}, {udp.Payload.Length} bytes, " +
                              $"checksum valid: {udp.IsChecksumValid(ipPacket)}");
        }
        #endregion

        Assert.AreEqual(IpProtocol.Udp, ipPacket.Protocol);
        Assert.AreEqual(5, ipPacket.ExtractUdp().Payload.Length);
    }

    [TestMethod]
    public void Ping()
    {
        #region Snippet:PacketsPing
        using var request = PacketBuilder.BuildIcmpEchoRequest(
            IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.1"), "ping"u8,
            identifier: 1, sequenceNumber: 1); // checksums computed

        // answer it as a host would: swap the addresses, keep the identifier, sequence number and data
        var echo = request.ExtractIcmpV4();
        using var reply = PacketBuilder.BuildIcmpEchoReply(
            request.DestinationAddress, request.SourceAddress, echo.Payload.Span,
            echo.Identifier, echo.SequenceNumber);
        #endregion

        Assert.IsTrue(reply.IsIcmpEcho());
        Assert.AreEqual(IcmpV4Type.EchoReply, reply.ExtractIcmpV4().Type);
        Assert.IsTrue(reply.ExtractIcmpV4().IsChecksumValid(reply));
    }
}
