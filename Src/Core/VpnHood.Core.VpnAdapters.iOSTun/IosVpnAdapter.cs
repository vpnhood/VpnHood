using Microsoft.Extensions.Logging;
using NetworkExtension;
using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.VpnAdapters.iOSTun;

public class IosVpnAdapter(NEPacketTunnelProvider tunnelProvider, IosVpnAdapterSettings settings)
    : TunVpnAdapter(settings)
{
    private NEPacketTunnelFlow? _packetFlow;
    private readonly List<NEIPv4Route> _ipv4Routes = [];
    private readonly List<NEIPv6Route> _ipv6Routes = [];
    private readonly List<IPAddress> _dnsServers = [];
    private readonly List<IpNetwork> _ipv4Networks = [];
    private readonly List<IpNetwork> _ipv6Networks = [];
    private int? _mtu;

    private readonly byte[] _writeBuffer = new byte[0xFFFF];
    // Reused single-element argument arrays for WritePackets. WritePacket runs on a single
    // thread (PacketTransportBase uses SingleReader and one send loop), and WritePackets copies
    // synchronously, so reusing these avoid allocating two managed arrays per packet.
    private readonly NSData[] _writeDataArray = new NSData[1];
    private readonly NSNumber[] _writeProtoArray = new NSNumber[1];

    // Cache the protocol-family numbers (AF_INET = 2, AF_INET6 = 30). Allocating a fresh
    // NSNumber per packet leaks native memory because the native peer is only freed on GC
    // finalization, which cannot keep up with full-traffic packet rates and pushes the
    // extension past the ~50 MB jetsam limit.
    private static readonly NSNumber AfInet = NSNumber.FromInt32(2);
    private static readonly NSNumber AfInet6 = NSNumber.FromInt32(30);

    // DIAGNOSTIC: per-direction packet counters so the host extension can tell whether traffic
    // actually flows through the tunnel ("connected but no traffic" diagnosis). Interlocked,
    // no per-packet allocation. Inbound = server->device (download), Outbound = device->server.
    public static long InboundPackets;
    public static long OutboundPackets;
    public static long InboundV6Packets;   // server->device IPv6 only
    public static long OutboundV6Packets;  // device->server IPv6 only

    protected override bool RestartAfterNetworkAddressChanged => false;
    public override bool IsNatSupported => false;
    public override bool IsAppFilterSupported => false;
    protected override bool IsSocketProtectedByBind => false;

    // Force-enable IPv6 support on iOS. Even if the physical interface is IPv4-only or
    // the server hands out a ULA virtual address (e.g. fd12:2020::/48), we want to provision
    // and route IPv6 traffic through the tunnel.
    public override bool IsIpVersionSupported(IpVersion ipVersion) => true;

    // CRITICAL (iOS): we CANNOT protect the tunnel's own transport socket the way desktop/
    // Android do (binding to the physical adapter IP does NOT make a raw .NET socket bypass
    // a NEPacketTunnelProvider tunnel; iOS only auto-excludes connections made via its own
    // NWTCPConnection/NWUDPSession APIs, which VpnHood does not use). If we reported
    // CanProtectSocket=true, core would leave the VPN server's IP inside the tunnel routes,
    // and once IPv4 routes are valid the server connection gets routed into its OWN tunnel
    // -> routing loop -> total freeze (no traffic in or out). Returning false makes core
    // EXCLUDE the server IP from the tunnel (ClientHelper.BuildIncludeIpRangesByDevice),
    // so the transport reaches the server over the physical interface.
    public override bool CanProtectSocket => false;

    protected override Task AdapterAdd(CancellationToken cancellationToken) => Task.CompletedTask;
    protected override void AdapterRemove() { }

    protected override async Task AdapterOpen(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Establishing iOS tun adapter...");

        // The tunnelRemoteAddress MUST be a valid IP address literal. Passing a non-IP
        // string (e.g. "vpnhood") makes iOS reject the ENTIRE settings object with
        // "Invalid NETunnelNetworkSettings tunnelRemoteAddress" in the completion handler,
        // so no routes are ever installed and no traffic enters the tunnel.
        // It is only informational for packet tunnels, so we prefer the real server IP
        // (from the protocol configuration) and fall back to a valid placeholder.
        var remoteAddress = "192.0.2.1";
        var serverAddress = tunnelProvider.ProtocolConfiguration?.ServerAddress;
        if (!string.IsNullOrEmpty(serverAddress) && IPAddress.TryParse(serverAddress, out _))
            remoteAddress = serverAddress;

        var settings = new NEPacketTunnelNetworkSettings(remoteAddress);

        // Set the default gateway for IPv4 and IPv6
        if (_ipv4Networks.Count > 0)
        {
            settings.IPv4Settings = new NEIPv4Settings(
                _ipv4Networks.Select(x => x.Prefix.ToString()).ToArray(),
                _ipv4Networks.Select(x => Ipv4MaskString(x.PrefixLength)).ToArray());

            // Install the core's routes VERBATIM. With CanProtectSocket=false the core
            // (ClientHelper.BuildIncludeIpRangesByDevice) has already split the default route into
            // many specific routes that cover the whole internet EXCEPT the active VPN server's own
            // IP (a single /32 hole, e.g. 15.204.89.227) and the local subnets, so the transport
            // reaches the server over the physical interface.
            //
            // CRITICAL: do NOT recombine these into a single 0.0.0.0/0 IncludedRoutes. Doing so
            // re-includes the server's /32 inside the tunnel (the ExcludedRoutes list cannot name it
            // — the real server IP is only known as the gap in this route set, not via ServerAddress,
            // which is the informational placeholder "1.1.1.1"). That loops the server connection
            // back into its own non-forwarding tunnel and freezes ALL traffic (the client gets stuck
            // in FindingReachableServer with 0 packets in/out). iOS treats this full split route set
            // as the default gateway and routes DNS through it correctly.
            settings.IPv4Settings.IncludedRoutes = _ipv4Routes.ToArray();
            VhLogger.Instance.LogDebug(
                "iOS: Configured IPv4 with {Count} routes from core (server IP excluded by omission).",
                _ipv4Routes.Count);
        }

        // Set the default gateway for IPv6
        if (_ipv6Networks.Count > 0)
        {
            settings.IPv6Settings = new NEIPv6Settings(
                _ipv6Networks.Select(x => x.Prefix.ToString()).ToArray(),
                _ipv6Networks.Select(x => NSNumber.FromInt32(x.PrefixLength)).ToArray());

            // iOS requires a global default IPv6 route (::/0) in IncludedRoutes to convince the
            // DNS resolver and routing engine that general IPv6 internet access is available when the
            // host physical link is IPv4-only. Without ::/0, iOS throttles or disables AAAA lookups.
            var routes = _ipv6Routes.ToList();
            if (!routes.Any(r => r is { DestinationAddress: "::", DestinationNetworkPrefixLength.Int32Value: 0 }))
            {
                routes.Add(new NEIPv6Route("::", 0));
            }
            settings.IPv6Settings.IncludedRoutes = routes.ToArray();

            // Exclude link-local to protect the USB CoreDevice pairing tunnel (keeps devicectl working)
            var excludedRoutes = new List<NEIPv6Route> { new NEIPv6Route("fe80::", 10) };
            if (!string.IsNullOrEmpty(serverAddress) && IPAddress.TryParse(serverAddress, out var parsedAddr) && parsedAddr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                excludedRoutes.Add(new NEIPv6Route(parsedAddr.ToString(), 128));
            }
            settings.IPv6Settings.ExcludedRoutes = excludedRoutes.ToArray();

            VhLogger.Instance.LogDebug("iOS: Configured IPv6 with {Count} routes (including injected default ::/0) and link-local exclusion.", routes.Count);
        }

        // Set DNS servers if any are provided
        if (_dnsServers.Count > 0)
            settings.DnsSettings = new NEDnsSettings(_dnsServers.Select(x=>x.ToString()) .ToArray());

        if (_mtu.HasValue)
        {
            settings.Mtu = NSNumber.FromInt32(_mtu.Value);
        }

        // DIAGNOSTIC PROBE: dump the routes/addresses/DNS we are about to install. This writes
        // synchronously (unlike the SetTunnelNetworkSettings completion callback, which often
        // never resumes under Mono AOT), so it is the reliable record of what we asked iOS for.
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var inc4 = settings.IPv4Settings?.IncludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationSubnetMask}") ?? [];
            var exc4 = settings.IPv4Settings?.ExcludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationSubnetMask}") ?? [];
            var inc6 = settings.IPv6Settings?.IncludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationNetworkPrefixLength.Int32Value}") ?? [];
            File.WriteAllText(Path.Combine(docs, "ext-route-dump.txt"),
                $"AdapterOpen at {DateTime.UtcNow:O}\n" +
                $"remoteAddress(ServerAddress)={remoteAddress}\n" +
                $"mtu={_mtu}\n" +
                $"v4 addrs({_ipv4Networks.Count}): {string.Join(", ", _ipv4Networks.Select(n => n.ToString()))}\n" +
                $"v4 routes-from-core({_ipv4Routes.Count}): {string.Join(", ", _ipv4Routes.Select(r => $"{r.DestinationAddress}/{r.DestinationSubnetMask}"))}\n" +
                $"v4 INCLUDED-applied: {string.Join(", ", inc4)}\n" +
                $"v4 EXCLUDED-applied: {string.Join(", ", exc4)}\n" +
                $"v6 addrs({_ipv6Networks.Count}): {string.Join(", ", _ipv6Networks.Select(n => n.ToString()))}\n" +
                $"v6 routes-from-core({_ipv6Routes.Count}): {string.Join(", ", _ipv6Routes.Select(r => $"{r.DestinationAddress}/{r.DestinationNetworkPrefixLength.Int32Value}"))}\n" +
                $"v6 INCLUDED-applied: {string.Join(", ", inc6)}\n" +
                $"dns({_dnsServers.Count}): {string.Join(", ", _dnsServers.Select(d => d.ToString()))}\n");
        }
        catch { /* best-effort */ }

        try
        {
            // CRITICAL: SetTunnelNetworkSettings MUST be called with a NON-NULL completion
            // handler AND on the main thread. iOS invokes the completion block internally;
            // a null block makes it dereference block->invoke at offset 0x10 → EXC_BAD_ACCESS
            // (SIGSEGV / KERN_INVALID_ADDRESS at 0x10). AdapterOpen runs on a background
            // VpnHood connect thread, where this crashes; the placeholder in StartTunnel
            // survived only because it ran on the main thread as the first call.
            // The managed callback may never resume under Mono AOT — that's fine, we don't
            // rely on it; we just give iOS a valid (non-null) block to invoke. We fire it on
            // the main thread and do not block waiting for the (possibly never-firing) callback.
            tunnelProvider.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    // err == null means iOS accepted and applied the routing settings.
                    tunnelProvider.SetTunnelNetworkSettings(settings, err => {
                        try
                        {
                            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            var result = err == null ? "Success" : $"Error: {err.LocalizedDescription} (code={err.Code})";
                            File.WriteAllText(Path.Combine(docs, "ext-probe-setsettings-result.txt"),
                                $"SetTunnelNetworkSettings completed at {DateTime.UtcNow:O} with: {result}\n");
                        }
                        catch { }
                    });
                }
                catch (Exception ex)
                {
                    try
                    {
                        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        File.WriteAllText(Path.Combine(docs, "ext-probe-setsettings-catch.txt"), ex.ToString());
                    }
                    catch { }
                }
            });
            await Task.Yield();
        }
        catch (Exception ex)
        {
            // Signal the deferred start-tunnel completion handler with the error so iOS
            // surfaces a failure instead of killing us silently.
            (tunnelProvider as IIosPacketTunnelProvider)?.CompleteStartTunnel(
                new NSError(new NSString("VpnHood"), 2,
                    NSDictionary.FromObjectAndKey((NSString)("SetTunnelNetworkSettings failed: " + ex.Message), NSError.LocalizedDescriptionKey)));
            throw;
        }

        _packetFlow = tunnelProvider.PacketFlow;
        VhLogger.Instance.LogDebug("iOS tun adapter has been established.");

        // Notify the deferred start-tunnel completion handler that the tunnel is live.
        (tunnelProvider as IIosPacketTunnelProvider)?.CompleteStartTunnel(null);
    }

    protected override void AdapterClose()
    {
        _ipv4Networks.Clear();
        _ipv6Networks.Clear();
        _ipv4Routes.Clear();
        _ipv6Routes.Clear();
        _dnsServers.Clear();
        _mtu = null;
        _packetFlow = null;
    }

    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        if (ipNetwork.IsV4)
            _ipv4Networks.Add(ipNetwork);
        else
            _ipv6Networks.Add(ipNetwork);

        return Task.CompletedTask;
    }

    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        if (ipNetwork.IsV4)
            _ipv4Routes.Add(new NEIPv4Route(ipNetwork.Prefix.ToString(), Ipv4MaskString(ipNetwork.PrefixLength)));
        else
            _ipv6Routes.Add(new NEIPv6Route(ipNetwork.Prefix.ToString(), ipNetwork.PrefixLength));

        return Task.CompletedTask;
    }

    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken) =>
        throw new NotSupportedException("iOS does not support NAT.");

    // Build a dotted-decimal IPv4 subnet mask in big-endian (network) order.
    // NOTE: do NOT use IpNetwork.SubnetMask here — its CidrToSubnetMask uses
    // byte[].Reverse() (LINQ, returns a new sequence that is discarded), leaving the
    // mask in little-endian order. That produces a byte-reversed mask (e.g. /12 -> 0.0.240.255
    // instead of 255.240.0.0). iOS silently rejects/ignores such IPv4 routes, so IPv4
    // traffic never enters the tunnel while IPv6 (which uses a prefix length) does.
    private static string Ipv4MaskString(int prefixLength)
    {
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _mtu = mtu;
        return Task.CompletedTask;
    }

    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        _dnsServers.AddRange(dnsServers);
        return Task.CompletedTask;
    }

    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        Task.CompletedTask; // MDM-only on iOS

    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        Task.CompletedTask; // MDM-only on iOS

    protected override string AppPackageId =>
        NSBundle.MainBundle.BundleIdentifier ?? throw new Exception("Could not get the app BundleIdentifier!");

    public override bool ProtectSocket(System.Net.Sockets.Socket socket) => true;

    public override bool ProtectSocket(System.Net.Sockets.Socket socket, IPAddress ipAddress) => true;

    protected override void WaitForTunRead() => Thread.Sleep(10);

    protected override void WaitForTunWrite() => Thread.Sleep(10);

    protected override bool WritePacket(IpPacket ipPacket)
    {
        if (_packetFlow == null)
            throw new InvalidOperationException("Packet flow is not initialized.");

        // CRITICAL (memory): WritePackets marshals the [data]/[protocolFamily] managed arrays
        // into temporary native NSArrays and NSData.FromArray creates internal native
        // temporaries — all AUTORELEASED. This runs on a background packet thread whose
        // autorelease pool is never drained by a run loop, so without our own pool those
        // native temporaries accumulate across thousands of packets and push the extension
        // past the ~50 MB jetsam limit (slow leak: ~50 MB after ~9000 writes). Drain per packet.
        using var pool = new NSAutoreleasePool();

        var buffer = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var offset, out var length);

        // todo: optimize by using batch after we get first IOs release
        // Inbound packets (server -> device) must use the IP version as the protocol
        // family (AF_INET = 2, AF_INET6 = 30); passing 0 makes iOS drop the packet.
        var isV6 = ipPacket.Version == IpVersion.IPv6;
        var protocolFamily = isV6 ? AfInet6 : AfInet;
        if (isV6) Interlocked.Increment(ref InboundV6Packets); // DIAGNOSTIC

        // CRITICAL (download memory): copy straight from the packet buffer into a native NSData
        // WITHOUT first allocating a managed slice (buffer[offset . . offset+length]). During a
        // download this path runs thousands of times per second; the old per-packet slice was
        // ~10 MB/s of managed garbage that the periodic GC could not reclaim within a sub-second
        // burst, spiking phys_footprint past the ~50 MB iOS extension jetsam limit. NSData.FromBytes
        // copies exactly `length` bytes from the pinned pointer, so nothing managed is allocated
        // for the payload. Reuse the single-element argument arrays too (single writer thread).
        NSData data;
        unsafe {
            fixed (byte* p = &buffer[offset])
                data = NSData.FromBytes((IntPtr)p, (nuint)length);
        }

        // Dispose the NSData immediately after WritePackets returns. WritePackets copies the
        // bytes synchronously into the tunnel, so the native NSData can be freed right away.
        // Without this, native memory accumulates until GC finalization (which lags far behind
        // the packet rate) and the extension is jetsam-killed at ~50 MB.
        _writeDataArray[0] = data;
        _writeProtoArray[0] = protocolFamily;
        _packetFlow.WritePackets(_writeDataArray, _writeProtoArray);
        data.Dispose();

        Interlocked.Increment(ref InboundPackets); // DIAGNOSTIC: server -> device (download)
        return true;
    }

    protected override void StartReadingPackets()
    {
        if (_packetFlow == null)
            throw new InvalidOperationException("Packet flow is not initialized.");

        _packetFlow.ReadPackets(OnPacketsReceived);
    }

    private void OnPacketsReceived(NSData[] packets, NSNumber[] protocols)
    {
        // Capture the flow up-front; AdapterClose may null it out while we run.
        var flow = _packetFlow;
        if (flow == null)
            return;

        // Drain native autoreleased temporaries created while parsing this batch so they do
        // not accumulate and push the extension past the ~50 MB jetsam limit.
        using (var pool = new NSAutoreleasePool())
        {
            foreach (var packetBuffer in packets)
            {
                try
                {
                    // todo: for better performance try to use bytes and convert by Marshal, lets do it later
                    var buffer = packetBuffer.ToArray();
                    var ipPacket = PacketBuilder.Parse(buffer);
                    if (ipPacket.Version == IpVersion.IPv6) Interlocked.Increment(ref OutboundV6Packets); // DIAGNOSTIC
                    OnPacketReceived(ipPacket);
                }
                catch
                {
                    // ignore malformed packet so a single bad packet never breaks the read loop
                }
                finally
                {
                    // Release the native NSData peer now instead of waiting for GC finalization.
                    packetBuffer.Dispose();
                }
            }

            // The protocol-family NSNumber wrappers are unused (we re-derive the family from the
            // parsed packet). Release their native peers now so they do not pile up either.
            foreach (var protocol in protocols)
                protocol.Dispose();
        }

        Interlocked.Add(ref OutboundPackets, packets.Length); // DIAGNOSTIC: device -> server (upload)

        // CRITICAL: ReadPackets is a ONE-SHOT API. It delivers a single batch of outbound
        // packets and then stops. We must re-arm it by calling it again here, otherwise the
        // device stops sending traffic into the tunnel after the first batch and routing
        // appears "connected but no traffic" (public IP never changes).
        flow.ReadPackets(OnPacketsReceived);
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        throw new NotSupportedException("Use StartReadingPackets as iOS already handle it.");
    }
}
