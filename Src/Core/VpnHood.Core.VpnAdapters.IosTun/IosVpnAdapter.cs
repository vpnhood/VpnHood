using Microsoft.Extensions.Logging;
using NetworkExtension;
using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.IosTun.Utils;

namespace VpnHood.Core.VpnAdapters.IosTun;

public class IosVpnAdapter(
    NEPacketTunnelProvider tunnelProvider, 
    IosVpnAdapterSettings settings, 
    Action<NSError?> completeStartTunnel)
    : TunVpnAdapter(settings)
{
    private NEPacketTunnelFlow? _packetFlow;
    private readonly List<IpNetwork> _ipv4Routes = [];
    private readonly List<IpNetwork> _ipv6Routes = [];
    private readonly List<IPAddress> _dnsServers = [];
    private readonly List<IpNetwork> _ipv4Networks = [];
    private readonly List<IpNetwork> _ipv6Networks = [];
    private int? _mtu;

    private readonly byte[] _writeBuffer = new byte[0xFFFF];
    // CRASH FIX (iOS, device-confirmed via symbolicated crash report): WritePacket reuses _writeBuffer,
    // _writeDataArray and _writeProtoArray and disposes the NSData immediately — that is only safe from
    // ONE thread. In TCP-proxy mode the user-space TCP stack emits inbound packets from many connection
    // pump threads concurrently, so concurrent WritePacket calls raced on these shared fields and handed
    // a disposed/garbled NSData to NEPacketTunnelFlow.WritePackets -> uncaught NSInvalidArgumentException
    // -> SIGABRT (extension dies at ANY memory level, not jetsam). Serialize the entire native write.
    private readonly Lock _writeLock = new();
    // Reused single-element argument arrays for WritePackets (safe under _writeLock); WritePackets copies
    // synchronously, so reusing this avoids allocating two managed arrays per packet.
    private readonly NSData[] _writeDataArray = new NSData[1];
    private readonly NSNumber[] _writeProtoArray = new NSNumber[1];

    // Cache the protocol-family numbers (AF_INET = 2, AF_INET6 = 30). Allocating a fresh
    // NSNumber per packet leaks native memory because the native peer is only freed on GC
    // finalization, which cannot keep up with full-traffic packet rates and pushes the
    // extension past the ~50 MB jetsam limit.
    private static readonly NSNumber AfInet = NSNumber.FromInt32(2);
    private static readonly NSNumber AfInet6 = NSNumber.FromInt32(30);

    // DIAGNOSTIC: cumulative bytes through the tunnel, read by IosVpnService's memory probe to
    // correlate phys_footprint growth with traffic. Use Interlocked to read/write.
    public static long InboundBytes;   // server -> device (download), written via WritePacket
    public static long OutboundBytes;  // device -> server (upload), read via OnPacketsReceived


    protected override bool RestartAfterNetworkAddressChanged => false;
    public override bool IsNatSupported => false;
    public override bool IsAppFilterSupported => false;
    protected override bool IsSocketProtectedByBind => false;

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
        var serverAddress = ServerIp?.ToString() ?? tunnelProvider.ProtocolConfiguration.ServerAddress;
        var remoteAddress = ServerIp ?? IPAddress.Parse("192.0.2.1");
        if (serverAddress != null && IPAddress.TryParse(serverAddress, out var parsedAddress))
            remoteAddress = parsedAddress;

        var settings = new NEPacketTunnelNetworkSettings(remoteAddress.ToString());

        // Set the default gateway for IPv4 and IPv6
        if (_ipv4Networks.Count > 0)
        {
            settings.IPv4Settings = new NEIPv4Settings(  
                _ipv4Networks.Select(x => x.Prefix.ToString()).ToArray(),
                _ipv4Networks.Select(x => x.SubnetMask.ToString()).ToArray());

            settings.IPv4Settings.IncludedRoutes = _ipv4Routes
                .Select(r => new NEIPv4Route(r.Prefix.ToString(), r.SubnetMask.ToString()))
                .ToArray();
            
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
            // routing engine that general IPv6 internet access is available when the
            // host physical link is IPv4-only. Without ::/0, iOS throttles or disables AAAA lookups.
            // So we need to ignore our include list
            // NOTE: On iOS, when there is no native IPv6, the default route ::/0 alone is not enough to convince the routing stack
            // to send IPv6 traffic to the tunnel. We must also explicitly include a specific global unicast route (2000::/3).
            // SPLIT-TUNNEL FIX (device-measured 2026-06-10): only inject the global v6 routes when core
            // actually asked for BROAD IPv6 coverage (a non-host include route). In route-level split
            // mode the include list is just host routes (e.g. DNS /128s) — injecting ::/0 there silently
            // dragged ALL IPv6 traffic (Safari prefers v6) into the tunnel, defeating the split.
            var wantsBroadV6 = _ipv6Routes.Any(r => r.PrefixLength < 64);
            var includes = IsIpVersionSupported(IpVersion.IPv6) || !wantsBroadV6
                ? _ipv6Routes
                : new List<IpNetwork> { IpNetwork.AllV6, IpNetwork.AllGlobalUnicastV6}.Concat(_ipv6Routes);
            
            // ReSharper disable once PossibleMultipleEnumeration
            settings.IPv6Settings.IncludedRoutes = includes
                .Select(r => new NEIPv6Route(r.Prefix.ToString(), r.PrefixLength))
                .ToArray();
           
            // ReSharper disable once PossibleMultipleEnumeration
            VhLogger.Instance.LogDebug("iOS: Configured IPv6 with {Count} routes (including injected default ::/0) and link-local exclusion.", includes.Count());
        }

        // Set DNS servers if any are provided
        if (_dnsServers.Count > 0)
            settings.DnsSettings = new NEDnsSettings(_dnsServers.Select(x=>x.ToString()) .ToArray());

        if (_mtu.HasValue)
            settings.Mtu = NSNumber.FromInt32(_mtu.Value);

        // DIAGNOSTIC PROBE: dump the routes/addresses/DNS we are about to install so the host
        // extension can verify what actually reaches iOS ("connected but no traffic" diagnosis).
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var inc4 = settings.IPv4Settings?.IncludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationSubnetMask}") ?? [];
            var exc4 = settings.IPv4Settings?.ExcludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationSubnetMask}") ?? [];
            var inc6 = settings.IPv6Settings?.IncludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationNetworkPrefixLength.Int32Value}") ?? [];
            var exc6 = settings.IPv6Settings?.ExcludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationNetworkPrefixLength.Int32Value}") ?? [];
            await File.WriteAllTextAsync(Path.Combine(docs, "ext-route-dump.txt"),
                $"AdapterOpen at {DateTime.UtcNow:O}\n" +
                $"remoteAddress(ServerAddress)={remoteAddress}\n" +
                $"mtu={_mtu}\n" +
                $"v4 address({_ipv4Networks.Count}): {string.Join(", ", _ipv4Networks.Select(n => n.ToString()))}\n" +
                $"v4 routes-from-core({_ipv4Routes.Count}): {string.Join(", ", _ipv4Routes.Select(r => $"{r.Prefix}/{r.PrefixLength}"))}\n" +
                $"v4 INCLUDED-applied: {string.Join(", ", inc4)}\n" +
                $"v4 EXCLUDED-applied: {string.Join(", ", exc4)}\n" +
                $"v6 address({_ipv6Networks.Count}): {string.Join(", ", _ipv6Networks.Select(n => n.ToString()))}\n" +
                $"v6 routes-from-core({_ipv6Routes.Count}): {string.Join(", ", _ipv6Routes.Select(r => $"{r.Prefix}/{r.PrefixLength}"))}\n" +
                $"v6 INCLUDED-applied: {string.Join(", ", inc6)}\n" +
                $"v6 EXCLUDED-applied: {string.Join(", ", exc6)}\n" +
                $"dns({_dnsServers.Count}): {string.Join(", ", _dnsServers.Select(d => d.ToString()))}\n", cancellationToken);
        }
        catch { /* best-effort */ }

        // CRITICAL: apply the network settings to iOS. Without this call the tunnel never
        // installs its routes, the OS-level tun is never brought up (no VPN status-bar
        // indicator), and NO traffic enters the tunnel — the client connects to the server
        // but every flow stays on the physical interface. (Regression: this call was
        // accidentally removed together with the route-dump probe in 97567f379.)
        // The binding auto-generates SetTunnelNetworkSettingsAsync from the completion-handler
        // overload: it throws NSErrorException when iOS reports a non-null NSError. iOS has no
        // native cancellation for this call, so WaitAsync only abandons the await on cancel.
        try
        {
            await tunnelProvider.SetTunnelNetworkSettingsAsync(settings).WaitAsync(cancellationToken);
        }
        catch (NSErrorException ex) {
            completeStartTunnel(ex.Error);
            throw;
        }
        catch (Exception ex)
        {
            // Signal the deferred start-tunnel completion handler with the error so iOS
            // surfaces a failure instead of killing us silently.
            completeStartTunnel(ex.ToNsExceptionError());
            throw;
        }

        _packetFlow = tunnelProvider.PacketFlow;
        VhLogger.Instance.LogDebug("iOS tun adapter has been established.");

        // Notify the deferred start-tunnel completion handler that the tunnel is live.
        completeStartTunnel(null);
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
            _ipv4Routes.Add(ipNetwork);
        else
            _ipv6Routes.Add(ipNetwork);

        return Task.CompletedTask;
    }

    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken) =>
        throw new NotSupportedException("iOS does not support NAT.");

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
        // Capture the flow; AdapterClose may null it out concurrently.
        var flow = _packetFlow ?? throw new InvalidOperationException("Packet flow is not initialized.");

        // CRASH FIX (iOS): serialize the ENTIRE native write — see _writeLock above.
        lock (_writeLock) {
            // CRITICAL (memory): WritePackets marshals the [data]/[protocolFamily] managed arrays
            // into temporary native NSArrays and NSData.FromArray creates internal native
            // temporaries — all AUTORELEASE. This runs on a background packet thread whose
            // autorelease pool is never drained by a run loop, so without our own pool those
            // native temporaries accumulate across thousands of packets and push the extension
            // past the ~50 MB jetsam limit (slow leak: ~50 MB after ~9000 writes). Drain per packet.
            using var pool = new NSAutoreleasePool();

            var buffer = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var offset, out var length);
            Interlocked.Add(ref InboundBytes, length);

            // todo: optimize by using batch after we get first IOs release
            // Inbound packets (server -> device) must use the IP version as the protocol
            // family (AF_INET = 2, AF_INET6 = 30); passing 0 makes iOS drop the packet.
            var isV6 = ipPacket.Version == IpVersion.IPv6;
            var protocolFamily = isV6 ? AfInet6 : AfInet;

            // CRITICAL (download memory): copy straight from the packet buffer into a native NSData
            // WITHOUT first allocating a managed slice (buffer[offset . . offset+length]). During a
            // download this path runs thousands of times per second; the old per-packet slice was
            // ~10 MB/s of managed garbage that the periodic GC could not reclaim within a sub-second
            // burst, spiking phys_footprint past the ~50 MB iOS extension jetsam limit. NSData.FromBytes
            // copies exactly `length` bytes from the pinned pointer, so nothing managed is allocated
            // for the payload.
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
            flow.WritePackets(_writeDataArray, _writeProtoArray);
            data.Dispose();
        }

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

        // Drain native autorelease temporaries created while parsing this batch so they do
        // not accumulate and push the extension past the ~50 MB jetsam limit.
        using var pool = new NSAutoreleasePool();

        foreach (var packetBuffer in packets)
        {
            try
            {
                // MEMORY (jetsam spike fix): parse straight from the native NSData bytes via a
                // ReadOnlySpan instead of NSData.ToArray(). ToArray allocated a throwaway managed
                // byte[] PER PACKET — pure gen0 garbage ON TOP OF the pooled copy PacketBuilder.Parse
                // already makes. Under an outbound burst those churned the managed heap faster than the
                // GC heartbeat could reclaim, and that transient (~+2 MB gcLive) on top of the ~42 MB
                // native floor is what tipped phys_footprint over the ~52 MB jetsam line. Parse copies
                // the span into a pooled buffer, so the span over native memory only has to stay valid
                // for the duration of the call (it does — packetBuffer is disposed below).
                var len = (int)packetBuffer.Length;
                Interlocked.Add(ref OutboundBytes, len);
                IpPacket ipPacket;
                unsafe {
                    ipPacket = PacketBuilder.Parse(new ReadOnlySpan<byte>((void*)packetBuffer.Bytes, len));
                }
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
