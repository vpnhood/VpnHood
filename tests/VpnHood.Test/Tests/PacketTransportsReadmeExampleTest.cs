using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Tests;

// VpnHood.Net.PacketTransports' README is a copy of the Snippet regions here, made by
// pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit a region, then run the script.
[TestClass]
public class PacketTransportsReadmeExampleTest
{
    #region Snippet:PacketTransportDevice
    // A transport over your own device: the queue, its sending loop and the packet ownership come from the base.
    public sealed class DeviceTransport() : PacketTransport(new PacketTransportOptions {
        AutoDisposePackets = true, // the base disposes each packet once SendPacketsAsync returns
        Blocking = false // a full queue drops its oldest packet instead of waiting for room
    })
    {
        public List<string> Written { get; } = [];

        // the sending loop hands over a batch at a time
        protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                Written.Add(ipPacket.ToString()); // write it to the device here
            return default;
        }

        // what the device reads goes up through PacketReceived
        public void OnDeviceRead(IpPacket ipPacket) => OnPacketReceived(ipPacket);
    }
    #endregion

    [TestMethod]
    [Timeout(30_000)]
    public async Task Send_and_receive()
    {
        #region Snippet:PacketTransportUse
        using var transport = new DeviceTransport();

        // one handler, which owns each received packet: it forwards or disposes it
        transport.PacketReceived += (_, ipPacket) => {
            Console.WriteLine($"Received: {ipPacket}");
            ipPacket.Dispose();
        };

        // a packet sent is the transport's from here, whatever the outcome
        var packet = PacketBuilder.BuildUdp(
            new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5353),
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 53),
            "query"u8);
        packet.UpdateAllChecksums();
        transport.SendPacketQueued(packet);
        #endregion

        while (transport.Written.Count == 0)
            await Task.Delay(10);
        Assert.AreEqual(1, transport.Written.Count);

        transport.OnDeviceRead(PacketBuilder.BuildUdp(
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 53),
            new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5353),
            "answer"u8));
        Assert.AreEqual(1, transport.PacketStat.ReceivedPackets);
    }
}
