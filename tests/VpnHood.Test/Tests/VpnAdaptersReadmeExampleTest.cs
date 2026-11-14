using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.WinTun;

namespace VpnHood.Test.Tests;

// VpnHood.Net.VpnAdapters.Abstractions' README is a copy of the Snippet regions here, made by
// pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit a region, then run the script.
[TestClass]
public class VpnAdaptersReadmeExampleTest
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task Start_send_stop()
    {
        #region Snippet:VpnAdapterStart
        // in an app, a platform's adapter: WinTunVpnAdapter, LinuxTunVpnAdapter, AndroidVpnAdapter...
        using var adapter = new NullVpnAdapter(blocking: false);

        // what the host routes into the tunnel arrives here, on the adapter's reader task
        adapter.PacketReceived += (_, ipPacket) => {
            Console.WriteLine($"From the host: {ipPacket}");
            ipPacket.Dispose(); // the handler owns each packet: forward it or dispose it
        };

        await adapter.Start(new VpnAdapterOptions {
            SessionName = "My VPN",
            VirtualIpNetworkV4 = IpNetwork.Parse("10.255.0.2/24"),
            DnsServers = [IPAddress.Parse("10.255.0.1")],
            IncludeNetworks = IpNetwork.All // every address of the IP versions the interface carries: IPv4 here
        }, CancellationToken.None);

        // what the VPN sends the host is queued; the adapter writes it on its own loop and disposes it
        var packet = PacketBuilder.BuildUdp(
            new IPEndPoint(IPAddress.Parse("10.255.0.1"), 53),
            new IPEndPoint(IPAddress.Parse("10.255.0.2"), 5353),
            "answer"u8);
        packet.UpdateAllChecksums();
        adapter.SendPacketQueued(packet);

        adapter.Stop(); // removes the interface, its routes and its DNS servers
        #endregion

        Assert.IsFalse(adapter.IsStarted);
    }

    // Compiled with the tests and never run: a WinTun adapter needs an administrator's rights, installs WinTun's
    // driver, and with IncludeNetworks = All would take the machine's traffic. VpnHood.Net.VpnAdapters.WinTun's README
    // and developer.vpnhood.com's home page show it.
    internal static async Task WinTunQuickStart(CancellationToken cancellationToken)
    {
        #region Snippet:VpnAdapterWinTun
        // LinuxTunVpnAdapter, AndroidVpnAdapter and IosVpnAdapter take the same calls
        using var adapter = new WinTunVpnAdapter(new WinVpnAdapterSettings {
            AdapterName = "MyVpn",
            Blocking = false // a full queue drops its oldest packet instead of making a send wait
        });

        // every packet the host routes into the tunnel arrives here
        adapter.PacketReceived += (_, ipPacket) => {
            Console.WriteLine($"{ipPacket.SourceAddress} -> {ipPacket.DestinationAddress}, {ipPacket.Protocol}");
            ipPacket.Dispose(); // the handler owns each packet: forward it or dispose it
        };

        await adapter.Start(new VpnAdapterOptions {
            SessionName = "My VPN",
            VirtualIpNetworkV4 = IpNetwork.Parse("10.255.0.2/24"),
            IncludeNetworks = IpNetwork.All
        }, cancellationToken);
        #endregion
    }
}
