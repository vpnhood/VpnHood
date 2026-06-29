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

    private const int MaxWriteBatchSize = 32;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];

    // The write path reuses _writeBuffer and the batch arrays, so native writes must be serialized.
    // Batching is especially important for TCP-proxy downloads where many MSS-sized packets are emitted
    // back-to-back into the iOS tunnel.
    private readonly Lock _writeLock = new();
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

    // ToDo: remove diagnose
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

        // ToDo: remove diagnose
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
        WriteSinglePacket(ipPacket);
        return true;
    }

    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        WritePacketBatch(ipPackets);
        return ValueTask.CompletedTask;
    }

    private void WritePacketBatch(IReadOnlyList<IpPacket> ipPackets)
    {
        // Capture the flow; AdapterClose may null it out concurrently.
        var flow = _packetFlow ?? throw new InvalidOperationException("Packet flow is not initialized.");
        if (ipPackets.Count == 0)
            return;

        lock (_writeLock) {
            // NEPacketTunnelFlow creates autoreleased native temporaries while marshaling arrays.
            // This runs on a packet worker, so keep an explicit pool around each send drain.
            using var pool = new NSAutoreleasePool();

            for (var batchStart = 0; batchStart < ipPackets.Count; batchStart += MaxWriteBatchSize) {
                var batchCount = Math.Min(MaxWriteBatchSize, ipPackets.Count - batchStart);
                try {
                    FillWriteBatch(ipPackets, batchStart, batchCount);
                    FlushWriteBatch(flow, batchCount);
                }
                finally {
                    ClearWriteBatch(batchCount);
                }
            }
        }
    }

    private void WriteSinglePacket(IpPacket ipPacket)
    {
        // Capture the flow; AdapterClose may null it out concurrently.
        var flow = _packetFlow ?? throw new InvalidOperationException("Packet flow is not initialized.");

        lock (_writeLock) {
            using var pool = new NSAutoreleasePool();
            try {
                FillWriteBatchSlot(ipPacket, 0);
                FlushWriteBatch(flow, 1);
            }
            finally {
                ClearWriteBatch(1);
            }
        }
    }

    private void FillWriteBatch(IReadOnlyList<IpPacket> ipPackets, int batchStart, int batchCount)
    {
        for (var i = 0; i < batchCount; i++)
            FillWriteBatchSlot(ipPackets[batchStart + i], i);
    }

    private void FillWriteBatchSlot(IpPacket ipPacket, int slot)
    {
        var buffer = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var offset, out var length);
        Interlocked.Add(ref InboundBytes, length);

        // Inbound packets (server -> device) must use the IP version as the protocol family.
        _writeProtocolBatch[slot] = ipPacket.Version == IpVersion.IPv6 ? AfInet6 : AfInet;

        // Copy directly from the packet buffer into native NSData. Avoiding a managed slice here
        // matters because this path runs at full packet rate during downloads.
        unsafe {
            fixed (byte* p = &buffer[offset])
                _writeDataBatch[slot] = NSData.FromBytes((IntPtr)p, (nuint)length);
        }
    }

    private void FlushWriteBatch(NEPacketTunnelFlow flow, int batchCount)
    {
        var dataBatch = _writeDataBatch;
        var protocolBatch = _writeProtocolBatch;

        if (batchCount != MaxWriteBatchSize) {
            dataBatch = _partialWriteDataBatches[batchCount];
            protocolBatch = _partialWriteProtocolBatches[batchCount];
            Array.Copy(_writeDataBatch, dataBatch, batchCount);
            Array.Copy(_writeProtocolBatch, protocolBatch, batchCount);
        }

        flow.WritePackets(dataBatch, protocolBatch);
    }

    private void ClearWriteBatch(int batchCount)
    {
        for (var i = 0; i < batchCount; i++) {
            _writeDataBatch[i]?.Dispose();
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
