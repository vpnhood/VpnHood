using Microsoft.Extensions.Logging;
using NetworkExtension;
using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.IosTun.Utils;

namespace VpnHood.Net.VpnAdapters.IosTun;

/// <summary>
/// Provides the VPN adapter for iOS and tvOS, inside an app's packet tunnel extension: it applies the tunnel's network
/// settings through the <see cref="NEPacketTunnelProvider"/>, and reads and writes packets through its
/// <see cref="NEPacketTunnelFlow"/>.
/// </summary>
/// <param name="tunnelProvider">The extension's packet tunnel provider.</param>
/// <param name="settings">The adapter's settings.</param>
/// <param name="completeStartTunnel">
/// The completion handler iOS gave the provider's start of the tunnel, wrapped so that only its first call counts, as
/// every open calls it: with <see langword="null"/> once the settings are applied, or with the error of applying them.
/// A failure before that, as while the settings are built, does not reach it: the caller answers iOS for such a one.
/// </param>
/// <remarks>
/// <para>
/// The start's addresses, routes, DNS servers and MTU are gathered and applied all at once when the adapter opens.
/// iOS hands the packets the device sends in batches, through callbacks, not through reads; the adapter writes in
/// batches of up to 32.
/// </para>
/// <para>
/// It has no NAT, no metric and no session name, and no app filter, which iOS leaves to device management. It cannot
/// protect a socket (<see cref="CanProtectSocket"/>): the extension's own sockets were seen to go around its tunnel on
/// an Apple TV, and the VPN keeps its server's address out of the routes in any case. Its steps take no notice of
/// their cancellation tokens, but the open's wait for iOS to apply the settings.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">
/// The settings' blocking timeout is negative or too long, or their queue capacity is not above zero.
/// </exception>
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

    // the process's, as a reconnect makes a new adapter under the tunnel still up, where only the tunnel's can be read
    private static IReadOnlyList<IPAddress> _networkDnsServers = [];

    private const int MaxWriteBatchSize = 32;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];

    // Core drains its send channel from a SINGLE consumer (Channel SingleReader=true, and this
    // transport is non-passthrough), so SendPacketsAsync is never invoked concurrently and this lock
    // is uncontended on the normal path. It is kept only as defensive serialization of the shared
    // _writeBuffer / batch arrays in case a direct or passthrough write path is ever added, and is
    // taken once per drain (not per packet), so the cost is negligible.
    // NOTE (history): an earlier comment claimed concurrent WritePacket calls from TCP forwarding pump
    // threads raced here. That diagnosis was wrong — pump threads only enqueue to the channel; the
    // native write has always been serialized by the single consumer. The real crash was a
    // garbled/null NSData handed to WritePackets, not a data race.
    private readonly Lock _writeLock = new();

    // WritePackets failure state, guarded by _writeLock; lets FlushWriteBatch log once per episode.
    private bool _writePacketsFailing;

    // Reused, allocation-free batch arrays. NEPacketTunnelFlow.WritePackets walks the FULL array
    // length, so a partial (final) chunk must be handed an exactly-sized array — a trailing null
    // would SIGABRT natively. _partialWrite*Batches[n] is a pre-allocated array of length n used for
    // a chunk of n<32; the full 32-wide arrays are used when a chunk is exactly MaxWriteBatchSize.
    private readonly NSData[] _writeDataBatch = new NSData[MaxWriteBatchSize];
    private readonly NSNumber[] _writeProtocolBatch = new NSNumber[MaxWriteBatchSize];
    private readonly NSData[][] _partialWriteDataBatches = CreatePartialWriteBatches<NSData>();
    private readonly NSNumber[][] _partialWriteProtocolBatches = CreatePartialWriteBatches<NSNumber>();

    // Cache the protocol-family numbers (AF_INET = 2, AF_INET6 = 30). Allocating a fresh
    // NSNumber per packet leaks native memory because the native peer is only freed on GC
    // finalization, which cannot keep up with full-traffic packet rates and pushes the
    // extension past the ~50 MB jetsam limit.
    private static readonly NSNumber AfInet = NSNumber.FromInt32(2);
    private static readonly NSNumber AfInet6 = NSNumber.FromInt32(30);

    // Traffic byte counters + the freeze locator (last TUN read/write stamps, worst write drain) live in
    // IosTunDiagnostics and are maintained only when IosTunDiagnostics.Enabled — see the call sites below.

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    protected override bool RestartAfterNetworkAddressChanged => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public override bool IsNatSupported(IpVersion ipVersion) => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>: iOS leaves the apps of a VPN to device management.</value>
    public override bool IsAppFilterSupported => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    protected override bool IsSocketProtectedByBind => false;

    /// <inheritdoc/>
    /// <value>
    /// Always <see langword="false"/>: the client then leaves its sockets out of its protection wrapper, while a direct
    /// call of <see cref="ProtectSocket(System.Net.Sockets.Socket, IPAddress)"/>, as its server address check makes,
    /// still gets <see langword="true"/>. The VPN leaves its server's address out of the routes in any case.
    /// </value>
    // ClientHelper.CreateSocketFactory reads it. The server's address leaves the routes whatever it says
    // (ClientHelper.BuildIncludeIpRangesByDevice): on an iPhone, with it inside them, the transport went into its own
    // tunnel and all traffic stopped, though on an Apple TV the extension's own sockets were seen going around it
    public override bool CanProtectSocket => false;

    /// <inheritdoc/>
    /// <remarks>It does nothing: the settings are applied when the adapter opens.</remarks>
    protected override Task AdapterAdd(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>It does nothing.</remarks>
    protected override void AdapterRemove() { }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// It reads the network's DNS servers first (<see cref="GetPrimaryAdapterDnsServers"/>), then applies the gathered
    /// addresses, routes, DNS servers and MTU to the tunnel, and calls the start's completion handler with
    /// <see langword="null"/>, before the reads start. A failure to apply them, or a canceled wait, goes to the handler
    /// too, as an <see cref="NSError"/>, and is thrown; one before, while the settings are built, is only thrown. The
    /// tunnel's remote address is the VPN server's, the protocol configuration's server address when it is an IP
    /// address, or <c>192.0.2.1</c>: iOS refuses settings whose remote address is not an IP address.
    /// </para>
    /// <para>
    /// When the tunnel has an IPv6 address and an IPv6 route shorter than /64, while the adapter found no IPv6 address
    /// on the network (<see cref="TunVpnAdapter.IsIpVersionSupported"/>), <c>::/0</c> and <c>2000::/3</c> join the
    /// routes, as iOS was seen to hold back AAAA lookups otherwise. Routes of /64 or narrower bring neither, so a split
    /// tunnel stays split, and iOS may then hold back AAAA lookups for them on a network with no IPv6.
    /// </para>
    /// </remarks>
    /// <exception cref="NSErrorException">iOS refused the settings.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: the wait ends, while iOS may still apply the settings.
    /// </exception>
    protected override async Task AdapterOpen(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Establishing iOS tun adapter...");
        TryReadNetworkDnsServers();

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

        using var networkSettings = new NEPacketTunnelNetworkSettings(remoteAddress.ToString());

        // Set the default gateway for IPv4 and IPv6
        if (_ipv4Networks.Count > 0) {
            using var ipv4Settings = new NEIPv4Settings(
                _ipv4Networks.Select(x => x.Prefix.ToString()).ToArray(),
                _ipv4Networks.Select(x => x.SubnetMask.ToString()).ToArray());

            var includedRoutes = _ipv4Routes
                .Select(r => new NEIPv4Route(r.Prefix.ToString(), r.SubnetMask.ToString()))
                .ToArray();
            try {
                ipv4Settings.IncludedRoutes = includedRoutes;
                networkSettings.IPv4Settings = ipv4Settings;
            }
            finally {
                foreach (var route in includedRoutes)
                    route.Dispose();
            }

            VhLogger.Instance.LogDebug(
                "iOS: Configured IPv4 with {Count} routes from core (server IP excluded by omission).",
                _ipv4Routes.Count);
        }

        // Set the default gateway for IPv6
        if (_ipv6Networks.Count > 0) {
            var prefixLengths = _ipv6Networks.Select(x => NSNumber.FromInt32(x.PrefixLength)).ToArray();
            NEIPv6Settings ipv6Settings;
            try {
                ipv6Settings = new NEIPv6Settings(
                    _ipv6Networks.Select(x => x.Prefix.ToString()).ToArray(), prefixLengths);
            }
            finally {
                foreach (var prefixLength in prefixLengths)
                    prefixLength.Dispose();
            }

            using (ipv6Settings) {

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
                // KNOWN CONSTRAINT: a /64-or-narrower include does not trigger injection, so on an
                // IPv4-only link iOS may suppress AAAA and hostname-based connections into that subnet
                // never try v6 (literal-IP connections still route). Do not "fix" this by loosening the
                // threshold — that re-breaks the split; if it ever bites, core must signal broad-v6
                // intent explicitly instead of the adapter inferring it from prefix lengths.
                var wantsBroadV6 = _ipv6Routes.Any(r => r.PrefixLength < 64);
                var injectGlobalV6 = !IsIpVersionSupported(IpVersion.IPv6) && wantsBroadV6;
                IReadOnlyList<IpNetwork> includes = injectGlobalV6
                    ? new[] { IpNetwork.AllV6, IpNetwork.AllGlobalUnicastV6 }
                        .Where(inj => !_ipv6Routes.Any(r =>
                            r.PrefixLength == inj.PrefixLength && r.Prefix.Equals(inj.Prefix)))
                        .Concat(_ipv6Routes)
                        .ToArray()
                    : _ipv6Routes;

                var includedRoutes = includes
                    .Select(r => new NEIPv6Route(r.Prefix.ToString(), r.PrefixLength))
                    .ToArray();
                try {
                    ipv6Settings.IncludedRoutes = includedRoutes;
                    networkSettings.IPv6Settings = ipv6Settings;
                }
                finally {
                    foreach (var route in includedRoutes)
                        route.Dispose();
                }

                VhLogger.Instance.LogDebug(
                    "iOS: Configured IPv6 with {Count} routes. GlobalV6RoutesInjected: {GlobalV6RoutesInjected}",
                    includes.Count, injectGlobalV6);
            }
        }

        // Set DNS servers if any are provided
        if (_dnsServers.Count > 0) {
            using var dnsSettings = new NEDnsSettings(_dnsServers.Select(x => x.ToString()).ToArray());
            networkSettings.DnsSettings = dnsSettings;
        }

        if (_mtu.HasValue) {
            using var mtu = NSNumber.FromInt32(_mtu.Value);
            networkSettings.Mtu = mtu;
        }

        // DIAGNOSTIC PROBE (no-op unless IosTunDiagnostics.Enabled): dump the routes/addresses/DNS we are
        // about to install so the host extension can verify what actually reaches iOS ("connected but no
        // traffic" diagnosis).
        await IosTunDiagnostics.WriteRouteDumpAsync(remoteAddress, _mtu, networkSettings,
            _ipv4Networks, _ipv4Routes, _ipv6Networks, _ipv6Routes, _dnsServers, cancellationToken).Vhc();

        // CRITICAL: apply the network settings to iOS. Without this call the tunnel never
        // installs its routes, the OS-level tun is never brought up (no VPN status-bar
        // indicator), and NO traffic enters the tunnel — the client connects to the server
        // but every flow stays on the physical interface. (Regression: this call was
        // accidentally removed together with the route-dump probe in 97567f379.)
        // The binding auto-generates SetTunnelNetworkSettingsAsync from the completion-handler
        // overload: it throws NSErrorException when iOS reports a non-null NSError. iOS has no
        // native cancellation for this call, so WaitAsync only abandons the await on cancel.
        try {
            await tunnelProvider.SetTunnelNetworkSettingsAsync(networkSettings).WaitAsync(cancellationToken).Vhc();
        }
        catch (NSErrorException ex) {
            completeStartTunnel(ex.Error);
            throw;
        }
        catch (Exception ex) {
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

    /// <inheritdoc/>
    /// <remarks>
    /// It forgets the gathered settings and the packet flow. It does not wait for a callback under way, which may still
    /// hand on its batch; a callback that finds the flow gone drops its batch and asks for no more. The settings applied
    /// to the tunnel stay.
    /// </remarks>
    protected override void AdapterClose()
    {
        // Clearing the config means a bare AdapterClose -> AdapterOpen would apply EMPTY settings.
        // The base never does that: its only recovery is a full Restart, which re-runs Start, and
        // that rebuilds these lists before AdapterOpen.
        _ipv4Networks.Clear();
        _ipv6Networks.Clear();
        _ipv4Routes.Clear();
        _ipv6Routes.Clear();
        _dnsServers.Clear();
        _mtu = null;
        _packetFlow = null;
    }

    /// <inheritdoc/>
    /// <remarks>It keeps the address for <see cref="AdapterOpen"/>, which applies the settings all at once.</remarks>
    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        if (ipNetwork.IsV4)
            _ipv4Networks.Add(ipNetwork);
        else
            _ipv6Networks.Add(ipNetwork);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It keeps the route for <see cref="AdapterOpen"/>, which applies the settings all at once.</remarks>
    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        if (ipNetwork.IsV4)
            _ipv4Routes.Add(ipNetwork);
        else
            _ipv6Routes.Add(ipNetwork);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: iOS has no NAT for an app.</exception>
    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken) =>
        throw new NotSupportedException("iOS does not support NAT.");

    /// <inheritdoc/>
    /// <remarks>It does nothing.</remarks>
    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>It does nothing.</remarks>
    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>It keeps the MTU for <see cref="AdapterOpen"/>, which applies it to both IP versions.</remarks>
    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _mtu = mtu;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It keeps the servers for <see cref="AdapterOpen"/>, which applies the settings all at once.</remarks>
    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        _dnsServers.AddRange(dnsServers);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: iOS leaves the apps of a VPN to device management.</remarks>
    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>It does nothing: iOS leaves the apps of a VPN to device management.</remarks>
    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    /// <value>The bundle identifier of the extension.</value>
    /// <exception cref="Exception">The extension's bundle has no identifier.</exception>
    protected override string AppPackageId =>
        NSBundle.MainBundle.BundleIdentifier ?? throw new Exception("Could not get the app BundleIdentifier!");

    /// <summary>Does nothing to the socket, and returns <see langword="true"/>.</summary>
    /// <param name="socket">The socket.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    /// <remarks>
    /// The extension's own sockets go around its tunnel, as a DNS query and a web request were seen to on an Apple TV
    /// with every route in the tunnel. The client's socket wrapper does not call it, as <see cref="CanProtectSocket"/> is
    /// <see langword="false"/>; its server address check does.
    /// </remarks>
    // So the server address check asks the network's DNS servers over a plain socket. The base's look for the
    // network's primary addresses goes around it too: on an iPhone it found the Wi-Fi's address, not the tunnel's,
    // with its target inside the tunnel's routes, so the session's check of the network's IP versions holds here.
    public override bool ProtectSocket(System.Net.Sockets.Socket socket) => true;

    /// <summary>
    /// Does nothing to the socket, and returns <see langword="true"/>, as <see cref="ProtectSocket(System.Net.Sockets.Socket)"/>
    /// does.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <param name="ipAddress">Not used.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public override bool ProtectSocket(System.Net.Sockets.Socket socket, IPAddress ipAddress) => true;

    /// <summary>
    /// Gets the DNS servers of the network under the tunnel: the last reading that found any, taken when an adapter
    /// opened, before it applied the tunnel's settings, as the device's resolver reports the tunnel's own servers once
    /// the tunnel serves the device's DNS.
    /// </summary>
    /// <returns>
    /// The servers, link-local IPv6 ones and those the opening start asked the tunnel for left out; none before a
    /// reading found any. They may be stale.
    /// </returns>
    /// <remarks>
    /// The reading is the process's: a reconnect makes a new adapter under the tunnel still up, whose open reads the
    /// tunnel's servers. Those are left out when the new start asks for the same ones, so the last reading of the
    /// network's stands; a start that asks for others can let the earlier tunnel's servers in.
    /// </remarks>
    public override IReadOnlyList<IPAddress> GetPrimaryAdapterDnsServers() => _networkDnsServers;

    // Read before the tunnel's settings take the device's DNS. An open under a tunnel still up, as a reconnect's, reads
    // the tunnel's, which are left out, so the last read of the network's stands.
    private void TryReadNetworkDnsServers()
    {
        try {
            var dnsServers = DeviceDnsServers.Read()
                .Where(x => !_dnsServers.Contains(x) && !x.IsIPv6LinkLocal)
                .ToArray();
            if (dnsServers.Length > 0)
                _networkDnsServers = dnsServers;

            VhLogger.Instance.LogInformation("The network's DNS servers: {DnsServers}",
                string.Join(", ", _networkDnsServers.Select(x => VhLogger.Format(x))));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read the network's DNS servers.");
        }
    }

    /// <inheritdoc/>
    /// <remarks>It sleeps 10 milliseconds.</remarks>
    protected override void WaitForTunRead() => Thread.Sleep(10);

    /// <inheritdoc/>
    /// <remarks>It sleeps 10 milliseconds.</remarks>
    protected override void WaitForTunWrite() => Thread.Sleep(10);

    /// <inheritdoc/>
    /// <remarks>
    /// It writes one packet through the packet flow; the adapter's sender writes with <see cref="SendPacketsAsync"/>
    /// instead.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> if the packet flow took the packet; <see langword="false"/> if it refused it.
    /// </returns>
    /// <exception cref="InvalidOperationException">The adapter is not open.</exception>
    protected override bool WritePacket(IpPacket ipPacket)
    {
        // Capture the flow; AdapterClose may null it out concurrently.
        var flow = _packetFlow ?? throw new InvalidOperationException("Packet flow is not initialized.");

        lock (_writeLock) {
            using var pool = new NSAutoreleasePool();
            try {
                FillWriteBatchSlot(ipPacket, 0);
                return FlushWriteBatch(flow, 1);
            }
            finally {
                ClearWriteBatch(1);
            }
        }
    }

    /// <summary>
    /// Writes a batch of packets to the packet flow, in chunks of up to 32, and is done when it returns.
    /// </summary>
    /// <param name="ipPackets">The batch. The list is reused after the call.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// A chunk iOS refuses is dropped and counted as dropped, which is logged once until writes succeed again; unlike
    /// the base's sender, no count of failures stops the adapter. An exception while a chunk is written ends the batch,
    /// which the base then counts as dropped whole.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The adapter is not open.</exception>
    // iOS ends a dead tunnel itself (stopTunnel), and Apple names no failure to stop on
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        // Capture the flow; AdapterClose may null it out concurrently.
        var flow = _packetFlow ?? throw new InvalidOperationException("Packet flow is not initialized.");
        if (ipPackets.Count == 0)
            return ValueTask.CompletedTask;

        // Freeze locator (diagnostics only): time the native write drain.
        var writeStart = IosTunDiagnostics.BeginTiming();

        lock (_writeLock) {
            // NEPacketTunnelFlow creates native autorelease temporaries while marshaling arrays.
            // This runs on a packet worker, so keep an explicit pool around each send drain.
            using var pool = new NSAutoreleasePool();

            for (var batchStart = 0; batchStart < ipPackets.Count; batchStart += MaxWriteBatchSize) {
                var batchCount = Math.Min(MaxWriteBatchSize, ipPackets.Count - batchStart);
                try {
                    FillWriteBatch(ipPackets, batchStart, batchCount);

                    // a chunk iOS refuses went nowhere: its packets are drops, not packets sent
                    if (!FlushWriteBatch(flow, batchCount))
                        for (var i = 0; i < batchCount; i++)
                            DropPacket(ipPackets[batchStart + i]);
                }
                finally {
                    ClearWriteBatch(batchCount);
                }
            }
        }

        IosTunDiagnostics.EndTunWrite(writeStart);

        return ValueTask.CompletedTask;
    }

    private void FillWriteBatch(IReadOnlyList<IpPacket> ipPackets, int batchStart, int batchCount)
    {
        for (var i = 0; i < batchCount; i++)
            FillWriteBatchSlot(ipPackets[batchStart + i], i);
    }

    private void FillWriteBatchSlot(IpPacket ipPacket, int slot)
    {
        var buffer = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var offset, out var length);
        IosTunDiagnostics.AddInboundBytes(length);

        // Inbound packets (server -> device) must use the IP version as the protocol family.
        _writeProtocolBatch[slot] = ipPacket.Version == IpVersion.IPv6 ? AfInet6 : AfInet;

        // Copy directly from the packet buffer into native NSData. Avoiding a managed slice here
        // matters because this path runs at full packet rate during downloads.
        // LOAD-BEARING COPY: FromBytes binds dataWithBytes: and duplicates the bytes inside this call;
        // the source is only valid right here — the fixed pin ends with the statement, a
        // non-array-backed packet's bytes sit in the shared _writeBuffer that the next slot
        // overwrites, and the packet's pooled buffer is disposed right after the drain while
        // WritePackets may retain the NSData. Never switch this to FromBytesNoCopy.
        unsafe {
            fixed (byte* p = &buffer[offset])
                _writeDataBatch[slot] = NSData.FromBytes((IntPtr)p, (nuint)length);
        }
    }

    private bool FlushWriteBatch(NEPacketTunnelFlow flow, int batchCount)
    {
        var dataBatch = _writeDataBatch;
        var protocolBatch = _writeProtocolBatch;

        if (batchCount != MaxWriteBatchSize) {
            dataBatch = _partialWriteDataBatches[batchCount];
            protocolBatch = _partialWriteProtocolBatches[batchCount];
            Array.Copy(_writeDataBatch, dataBatch, batchCount);
            Array.Copy(_writeProtocolBatch, protocolBatch, batchCount);
        }

        var ok = flow.WritePackets(dataBatch, protocolBatch);

        // Log transitions only; a dead flow would otherwise log every batch.
        if (ok == _writePacketsFailing) {
            if (ok)
                VhLogger.Instance.LogInformation("iOS: NEPacketTunnelFlow.WritePackets recovered.");
            else
                VhLogger.Instance.LogWarning("iOS: NEPacketTunnelFlow.WritePackets returned false; packets are being dropped.");
        }
        _writePacketsFailing = !ok;

        return ok;
    }

    private void ClearWriteBatch(int batchCount)
    {
        for (var i = 0; i < batchCount; i++) {
            // null-conditional is required: this runs in a finally block, and a mid-batch fill
            // failure (garbled packet) leaves the remaining slots null — a plain Dispose would
            // replace the real exception with a NullReferenceException.
            _writeDataBatch[i].Dispose();
            _writeDataBatch[i] = null!;
            _writeProtocolBatch[i] = null!;
        }

        if (batchCount == MaxWriteBatchSize)
            return;

        Array.Clear(_partialWriteDataBatches[batchCount]);
        Array.Clear(_partialWriteProtocolBatches[batchCount]);
    }

    private static T[][] CreatePartialWriteBatches<T>()
    {
        var batches = new T[MaxWriteBatchSize][];
        for (var i = 0; i < batches.Length; i++)
            batches[i] = new T[i];
        return batches;
    }

    /// <summary>
    /// Asks iOS for the packets the device sends: each batch arrives in a callback, which hands its packets to the
    /// transport's receivers and asks for the next batch.
    /// </summary>
    /// <remarks>
    /// A packet that does not parse is dropped. With no packet flow, as during a stop, it does nothing, and logs a
    /// warning when the adapter is started.
    /// </remarks>
    protected override void StartReadingPackets()
    {
        // Base fires this from a fire-and-forget Task.Run after AdapterOpen, so a throw here would
        // fault an unobserved task and vanish. A null flow during a concurrent Stop is normal;
        // a null flow on a started adapter means outbound traffic would silently never be read
        // ("connected but no traffic"), so make that case loud.
        var flow = _packetFlow;
        if (flow == null) {
            if (IsStarted)
                VhLogger.Instance.LogWarning(
                    "iOS: no packet flow at StartReadingPackets; outbound traffic will not be read.");
            return;
        }

        flow.ReadPackets(OnPacketsReceived);
    }

    private void OnPacketsReceived(NSData[] packets, NSNumber[] protocols)
    {
        // Freeze locator (diagnostics only): stamp the outbound-read callback time.
        IosTunDiagnostics.MarkTunReadCallback(packets.Length);

        // Drain native autorelease temporaries created while parsing this batch so they do
        // not accumulate and push the extension past the ~50 MB jetsam limit.
        using var pool = new NSAutoreleasePool();

        // AdapterClose may race a callback that iOS already queued. The batch still belongs to this
        // callback and must be released even though it must no longer be delivered or re-armed.
        if (_packetFlow == null) {
            foreach (var packetBuffer in packets)
                packetBuffer.Dispose();
            foreach (var protocol in protocols)
                protocol.Dispose();
            return;
        }

        foreach (var packetBuffer in packets) {
            try {
                // MEMORY (jetsam spike fix): parse straight from the native NSData bytes via a
                // ReadOnlySpan instead of NSData.ToArray(). ToArray allocated a throwaway managed
                // byte[] PER PACKET — pure gen0 garbage ON TOP OF the pooled copy PacketBuilder.Parse
                // already makes. Under an outbound burst those churned the managed heap faster than the
                // GC heartbeat could reclaim, and that transient (~+2 MB gcLive) on top of the ~42 MB
                // native floor is what tipped phys_footprint over the ~52 MB jetsam line. Parse copies
                // the span into a pooled buffer, so the span over native memory only has to stay valid
                // for the duration of the call (it does — packetBuffer is disposed below).
                var len = (int)packetBuffer.Length;
                IosTunDiagnostics.AddOutboundBytes(len);
                IpPacket ipPacket;
                unsafe {
                    ipPacket = PacketBuilder.Parse(new ReadOnlySpan<byte>((void*)packetBuffer.Bytes, len));
                }
                OnPacketReceived(ipPacket);
            }
            catch {
                // ignore malformed packet so a single bad packet never breaks the read loop
            }
            finally {
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
        // Re-arm via the field, not a captured flow: if AdapterClose ran while this batch was
        // processing, the chain ends here instead of registering one more dead callback that
        // could swallow a batch meant for a successor adapter.
        _packetFlow?.ReadPackets(OnPacketsReceived);
    }

    /// <summary>Not used: iOS hands the packets to <see cref="StartReadingPackets"/>'s callbacks.</summary>
    /// <param name="buffer">Not used.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    protected override int ReadPacket(byte[] buffer)
    {
        throw new NotSupportedException("Use StartReadingPackets as iOS already handle it.");
    }
}
