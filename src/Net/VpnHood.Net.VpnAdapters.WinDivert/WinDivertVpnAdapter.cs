using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpPcap;
using SharpPcap.WinDivert;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Collections;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.WinDivert;

/// <summary>
/// Provides a Windows VPN adapter on WinDivert, a packet capture driver, with no interface of its own: it takes the
/// host's outbound packets to the routed networks before they leave, and puts the VPN's packets back in as inbound.
/// </summary>
/// <param name="adapterSettings">The adapter's settings.</param>
/// <remarks>
/// <para>
/// It serves tests and debugging; a Windows app runs on <c>WinTunVpnAdapter</c>. A captured packet's source becomes
/// the adapter's virtual address, and a packet the VPN sends back goes in as inbound, addressed to the host's primary
/// address. The host's DNS settings stay as they are: with <see cref="WinDivertVpnAdapterSettings.SimulateDns"/>,
/// every outbound UDP packet to port 53, wherever it goes, is captured too, but loopback ones and those of
/// <see cref="ProtectedTtl"/>, and sent to one of the adapter's DNS servers of its IP version, if it has one; an answer
/// comes back from the address the latest query from its port was sent to, while that is kept.
/// </para>
/// <para>
/// It runs as an administrator only, on x64 Windows in an x64 process: the first start in a process installs the
/// WinDivert DLL and driver it carries into a protected folder of their own under Common Files, or takes the ones
/// installed there, and the driver stays loaded until a reboot or until someone stops it. A socket is kept out of the
/// capture by its TTL, <see cref="ProtectedTtl"/>. It has no NAT, no app filter and no interface, so no address, MTU,
/// metric or session name of its own: the virtual address only rewrites the captured packets. It writes no packet
/// before it has captured one, whose header the written packets reuse.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">
/// The settings' blocking timeout is negative or too long, or their queue capacity is not above zero.
/// </exception>
public class WinDivertVpnAdapter(WinDivertVpnAdapterSettings adapterSettings) :
    TunVpnAdapter(adapterSettings)
{
    private static readonly Lock WinDivertDllLock = new();
    private static readonly Lock WinDivertOpenLock = new();
    private static IntPtr _winDivertDll; // the one WinDivert.dll of the process, once loaded

    // AdapterOpen is atomic against AdapterClose and AdapterRemove: a stop during the open would find no handle
    // yet and drop the device, and the handle the open then made would hold the host's matching traffic unread
    private readonly Lock _deviceLock = new();
    private WinDivertDevice? _device;
    private WinDivertHeader? _lastCaptureHeader;
    private readonly List<IpNetwork> _includeIpNetworks = [];
    private IReadOnlyList<IPAddress> _dnsServers = [];

    private readonly TimeoutDictionary<ushort, TimeoutItem<IPAddress>>
        _lastDnsServersV4 = new(TimeSpan.FromSeconds(30));

    private readonly TimeoutDictionary<ushort, TimeoutItem<IPAddress>>
        _lastDnsServersV6 = new(TimeSpan.FromSeconds(30));

    private readonly bool _excludeLocalNetwork = adapterSettings.ExcludeLocalNetwork;
    private readonly bool _simulateDns = adapterSettings.SimulateDns;

    /// <summary>
    /// The TTL, or IPv6 hop limit, that marks a protected socket's packets, which the capture leaves alone: 111.
    /// </summary>
    public const short ProtectedTtl = 111;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    public override bool IsAppFilterSupported => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>: a socket is protected by its TTL.</value>
    protected override bool IsSocketProtectedByBind => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public override bool IsNatSupported(IpVersion ipVersion) => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: there is no app filter.</value>
    protected override string? AppPackageId => null;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    protected override bool RestartAfterNetworkAddressChanged => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>: there is no interface to check.</value>
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => false;

    /// <inheritdoc/>
    /// <remarks>
    /// There is no interface to make: the first start in a process loads WinDivert, installing it first where needed.
    /// </remarks>
    /// <exception cref="PlatformNotSupportedException">
    /// The OS is not x64 Windows, or the process is not x64.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The process is not an administrator's, or Common Files or the install folder is a link or one others than
    /// administrators may change, so WinDivert cannot be installed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The package's WinDivert files are missing, or the installed ones are not the ones the package carries, are
    /// links, or are files others than administrators may change.
    /// </exception>
    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        LoadWinDivertDll();

        // initialize devices
        _device = new WinDivertDevice { Flags = 0 };

        // clean old configs
        _includeIpNetworks.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It closes and releases the capture.</remarks>
    protected override void AdapterRemove()
    {
        lock (_deviceLock) {
            AdapterClose();

            // SharpPcap's dispose closes the device again, which throws as the close does
            _device.TryDispose(LogLevel.Warning);
            _device = null;
        }
    }

    private static string Ip(IpRange ipRange)
    {
        return ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ip";
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It opens the capture of the host's outbound packets, but loopback ones and those of <see cref="ProtectedTtl"/>:
    /// those to the routes' networks, less the local networks with
    /// <see cref="WinDivertVpnAdapterSettings.ExcludeLocalNetwork"/>, and, with
    /// <see cref="WinDivertVpnAdapterSettings.SimulateDns"/>, every UDP packet to port 53, local or not. The first open
    /// loads WinDivert's driver.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A stop removed the capture while the start was under way.</exception>
    /// <exception cref="Exception">
    /// The open failed with an error whose message says, in English, that access is denied, as when the process is not
    /// an administrator's.
    /// </exception>
    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        var ipRanges = _includeIpNetworks.ToIpRanges();
        if (_excludeLocalNetwork)
            ipRanges = ipRanges.Exclude(IpNetwork.LocalNetworks.ToIpRanges());

        // create include and exclude phrases
        var phraseX = "true";
        if (!ipRanges.IsAll()) {
            var phrases = ipRanges.Select(x => x.FirstIpAddress.Equals(x.LastIpAddress)
                ? $"{Ip(x)}.DstAddr=={x.FirstIpAddress}"
                : $"({Ip(x)}.DstAddr>={x.FirstIpAddress} and {Ip(x)}.DstAddr<={x.LastIpAddress})");
            var phrase = string.Join(" or ", phrases);
            phraseX += $" and ({phrase})";
            phraseX = $"({phraseX})";
        }

        // simulate dns servers
        var dnsPhrase = _simulateDns ? "(udp.DstPort==53)" : "false";

        // add outbound; filter loopback
        var filter = $"(ip.TTL!={ProtectedTtl} or ipv6.HopLimit!={ProtectedTtl}) and " +
                     $"outbound and !loopback and " +
                     $"({dnsPhrase} or {phraseX})";

        filter = filter.Replace("ipv6.DstAddr>=::", "ipv6"); // WinDivert bug
        try {
            lock (_deviceLock) {
                if (_device == null)
                    throw new InvalidOperationException("Could not open WinDivert. The device has been removed.");

                _device.Filter = filter;

                // One open at a time in the process: with the driver not loaded, the first open installs and
                // starts it, and a second that found none meanwhile fails to start it again (error 1058). Two
                // processes opening at that moment still can; left so: a reconnect opens the running driver
                lock (WinDivertOpenLock)
                    _device.Open(new DeviceConfiguration());
            }
        }
        catch (Exception ex) {
            if (ex.Message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception(
                    "Access denied! Could not open WinDivert driver! Make sure the app is running with admin privilege.",
                    ex);
            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>A failure to close the capture is logged, not thrown.</remarks>
    protected override void AdapterClose()
    {
        // SharpPcap throws when the handle's close fails; the adapter's removal goes on all the same
        lock (_deviceLock)
            VhInvoker.TryInvoke("close the WinDivert device", LogLevel.Warning, () => _device?.Close());
    }


    /// <inheritdoc/>
    /// <remarks>
    /// It keeps the servers for <see cref="WinDivertVpnAdapterSettings.SimulateDns"/>; the host's own DNS settings stay
    /// as they are.
    /// </remarks>
    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        _dnsServers = [.. dnsServers];
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It adds the network to the capture, which the open then makes.</remarks>
    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        _includeIpNetworks.Add(ipNetwork);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: there is no app filter.</exception>
    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: there is no app filter.</exception>
    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    /// <inheritdoc/>
    /// <remarks>It does nothing: there is no interface.</remarks>
    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: there is no interface.</remarks>
    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: there is no interface.</remarks>
    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: there is no NAT.</exception>
    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("NAT is not supported on LinuxTun.");
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: there is no interface MTU to set.</remarks>
    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>Not used: the adapter reads with <see cref="ReadPacket(int)"/>, which WinDivert sizes.</summary>
    /// <param name="buffer">Not used.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    protected override int ReadPacket(byte[] buffer)
    {
        throw new InvalidOperationException(
            "ReadPacket with buffer should not be called when ReadPacket has the override.");
    }

    /// <summary>
    /// Blocks until WinDivert captures a packet, then returns it as <see cref="ProcessReadPacket"/> rewrites it.
    /// </summary>
    /// <param name="mtu">Not used: WinDivert sizes the packet.</param>
    /// <returns>The packet, which owns its bytes; never <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The capture is not made, or WinDivert gave no packet, as after the capture closed.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The captured bytes are no valid IP packet, or its payload is invalid for its protocol, which the rewrite's
    /// checksums read.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// With <see cref="WinDivertVpnAdapterSettings.SimulateDns"/> and DNS servers, the packet is a UDP fragment, which
    /// the DNS rewrite cannot take apart.
    /// </exception>
    protected override IpPacket ReadPacket(int mtu)
    {
        if (_device == null)
            throw new InvalidOperationException("Device is not initialized.");

        // a blocking read gives a packet or fails: any other status is an error for the reader to count, as
        // a null would loop at once, uncounted (ERROR_NO_DATA after a shutdown of the handle would spin it)
        var status = _device.GetNextPacket(out var packetCapture);
        if (status != GetPacketStatus.PacketRead)
            throw new InvalidOperationException($"Could not read from WinDivert. Status: {status}");

        _lastCaptureHeader = (WinDivertHeader)packetCapture.Header;
        var rawPacket = packetCapture.GetPacket();
        var ipPacket = PacketBuilder.Attach(rawPacket.Data);

        ProcessReadPacket(ipPacket);
        return ipPacket;
    }

    /// <summary>
    /// Keeps a socket's traffic out of the capture by binding it to any address and giving it <see cref="ProtectedTtl"/>.
    /// Call it before the socket is bound or connected.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    /// <exception cref="SocketException">The socket could not be bound, as when it is bound already.</exception>
    /// <exception cref="ObjectDisposedException">The socket is closed.</exception>
    public override bool ProtectSocket(Socket socket)
    {
        var ipAddress = socket.AddressFamily.IsV4 ? IPAddress.Any : IPAddress.IPv6Any;
        socket.Bind(new IPEndPoint(ipAddress, 0));
        socket.Ttl = ProtectedTtl;
        return true;
    }

    /// <summary>
    /// Keeps a socket's traffic out of the capture, as <see cref="ProtectSocket(Socket)"/> does: the remote address does
    /// not matter, loopback included. Call it before the socket is bound or connected.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <param name="ipAddress">Not used.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    /// <exception cref="SocketException">The socket could not be bound, as when it is bound already.</exception>
    /// <exception cref="ObjectDisposedException">The socket is closed.</exception>
    public override bool ProtectSocket(Socket socket, IPAddress ipAddress)
    {
        // WinDivert protection is done purely by the TTL marker (the capture filter excludes
        // ProtectedTtl), not by binding to a specific adapter IP. Route the same way as the
        // single-argument overload so both paths are consistent and the return value is truthful.
        return ProtectSocket(socket);
    }

    /// <summary>
    /// Rewrites a captured packet for the VPN: its source becomes the adapter's virtual address of its IP version, and,
    /// with <see cref="WinDivertVpnAdapterSettings.SimulateDns"/>, a DNS query's destination one of the adapter's DNS
    /// servers of that version, chosen at random, if it has one.
    /// </summary>
    /// <param name="ipPacket">The packet, changed in place, with its checksums updated.</param>
    /// <remarks>
    /// A packet of an IP version the adapter has no virtual address of keeps its source. A query's destination is kept
    /// for its answer by its IP version and source port, the latest for each, until 30 seconds pass with no query or
    /// answer using it.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The packet's payload is invalid for its protocol, which the rewrite's checksums read.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// With <see cref="WinDivertVpnAdapterSettings.SimulateDns"/> and DNS servers, the packet is a UDP fragment, which
    /// the DNS rewrite cannot take apart.
    /// </exception>
    protected virtual void ProcessReadPacket(IpPacket ipPacket)
    {
        // simulate adapter network
        SimulateAdapterNetwork(ipPacket, true);

        // simulate dns servers
        SimulateDnsServers(ipPacket, true);
    }


    /// <summary>
    /// Writes one packet to the host as inbound, and blocks until WinDivert takes it: its destination becomes the host's
    /// primary address and, with <see cref="WinDivertVpnAdapterSettings.SimulateDns"/>, a DNS answer's source becomes the
    /// address the latest query from its port was sent to, while that is kept.
    /// </summary>
    /// <param name="ipPacket">The packet, changed in place, with its checksums updated.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// No packet has been captured yet, the capture is not made, or the host has no primary address of the packet's IP
    /// version.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The packet's payload is invalid for its protocol, which the rewrite's checksums read.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// With <see cref="WinDivertVpnAdapterSettings.SimulateDns"/> and DNS servers, the packet is a UDP fragment.
    /// </exception>
    protected override bool WritePacket(IpPacket ipPacket)
    {
#if DEBUG
        if (GetIpNetwork(ipPacket.Version)?.Contains(ipPacket.DestinationAddress) is null or false)
            throw new NotSupportedException("This adapter can not send packets outside of its network.");
#endif

        // simulate adapter network
        SimulateAdapterNetwork(ipPacket, false);

        // simulate dns servers
        SimulateDnsServers(ipPacket, false);

        // Write packet
        WritePacketToAdapter(ipPacket, false);
        return true;
    }

    /// <summary>
    /// Writes a packet to WinDivert as it is, with the header of the last packet captured, whose interface WinDivert
    /// uses for an inbound packet.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    /// <param name="outbound">
    /// <see langword="true"/> to send it out from the host; <see langword="false"/> to deliver it to the host as
    /// inbound.
    /// </param>
    /// <exception cref="InvalidOperationException">No packet has been captured yet, or the capture is not made.</exception>
    protected void WritePacketToAdapter(IpPacket ipPacket, bool outbound)
    {
        // Snapshot the last capture header into a local. The reader thread can reassign
        // _lastCaptureHeader between our flag-set and the send below; without the snapshot we could set
        // the flag on one header object but hand a different (freshly captured, Outbound) header to
        // SendPacket, reinjecting with the wrong direction/interface.
        var captureHeader = _lastCaptureHeader
            ?? throw new InvalidOperationException("Could not send any data without receiving a packet.");

        if (_device == null)
            throw new InvalidOperationException("Device is not initialized.");

        // send by a device
        captureHeader.Flags = outbound ? WinDivertPacketFlags.Outbound : 0;
        _device.SendPacket(ipPacket.Buffer.Span, captureHeader);
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: the read itself blocks.</remarks>
    protected override void WaitForTunRead()
    {
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: the write itself blocks.</remarks>
    protected override void WaitForTunWrite()
    {
    }

    private void SimulateAdapterNetwork(IpPacket ipPacket, bool read)
    {
        if (read) {
            var adapterIp = GetIpNetwork(ipPacket.Version)?.Prefix;
            if (adapterIp == null) {
                VhLogger.Instance.LogDebug("The arrival packet is not supported : {Packet}",
                    VhLogger.FormatIpPacket(ipPacket.ToString()));
                return;
            }

            ipPacket.SourceAddress = adapterIp;
        }
        else {
            var primaryAdapterIp = GetPrimaryAdapterAddress(ipPacket.Version);
            if (primaryAdapterIp == null)
                throw new InvalidOperationException("Could not send packet to inbound. there is no internal IP.");

            ipPacket.DestinationAddress = primaryAdapterIp;
        }

        ipPacket.UpdateAllChecksums();
    }

    private void SimulateDnsServers(IpPacket ipPacket, bool read)
    {
        if (!_simulateDns)
            return;

        // Only rewrite when we actually have DNS servers to redirect to; otherwise there is nothing to
        // simulate (the read branch below needs a non-empty pool to pick a target from).
        if (ipPacket.Protocol != IpProtocol.Udp || !_dnsServers.Any())
            return;

        var udpPacket = ipPacket.ExtractUdp();
        var lastDnsServers = ipPacket.Version == IpVersion.IPv4 ? _lastDnsServersV4 : _lastDnsServersV6;

        if (read) {
            // check if the packet is a dns query
            if (udpPacket.DestinationPort != 53)
                return;

            // update the last dns server
            lastDnsServers.AddOrUpdate(udpPacket.SourcePort, new TimeoutItem<IPAddress>(ipPacket.DestinationAddress));

            // select a random dns server by matching its ip version
            var filteredDnsServers = _dnsServers
                .Where(dns => dns.AddressFamily == ipPacket.SourceAddress.AddressFamily)
                .ToArray();

            // select a random dns server
            var dnsServer = filteredDnsServers.Length > 0
                ? filteredDnsServers[Random.Shared.Next(filteredDnsServers.Length)]
                : null;

            // update the dns server
            if (dnsServer != null) {
                ipPacket.DestinationAddress = dnsServer;
                ipPacket.UpdateAllChecksums();
            }
        }
        else {
            // check if the packet is a dns response
            if (udpPacket.SourcePort != 53)
                return;

            // update the last dns server
            if (lastDnsServers.TryGetValue(udpPacket.DestinationPort, out var dnsServer))
                ipPacket.SourceAddress = dnsServer.Value;

            ipPacket.UpdateAllChecksums();
        }
    }


    // The embedded DLL and driver, installed into a protected folder of their own under Common Files: the
    // DLL starts the driver from the file beside it, which then stays in use until a reboot or
    // "sc stop WinDivert", and there blocks no app's update or cleanup. A driver already running is used
    // as it is, wherever it came from; it is shared, so never stopped here. SharpPcap's imports bind to
    // this module by name: they are its own, so no resolver of ours ties them.
    private static void LoadWinDivertDll()
    {
        lock (WinDivertDllLock) {
            if (_winDivertDll != IntPtr.Zero)
                return;

            // a driver matches Windows itself, not only the process, and this one is x64 only
            if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 ||
                RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("WinDivert runs on x64 Windows, in an x64 process, only.");

            using var dllResource = OpenResource("WinDivert.dll");
            using var driverResource = OpenResource("WinDivert64.sys");
            var folderPath = WindowsCommonFiles.Install("WinDivert-x64", new Dictionary<string, Stream> {
                ["WinDivert.dll"] = dllResource,
                ["WinDivert64.sys"] = driverResource
            });

            _winDivertDll = NativeLibrary.Load(Path.Combine(folderPath, "WinDivert.dll"),
                typeof(WinDivertVpnAdapter).Assembly,
                DllImportSearchPath.System32 | DllImportSearchPath.UseDllDirectoryForDependencies);
        }
    }

    private static Stream OpenResource(string name)
    {
        return typeof(WinDivertVpnAdapter).Assembly.GetManifestResourceStream(name) ??
               throw new InvalidOperationException($"The embedded {name} is missing.");
    }

    /// <inheritdoc/>
    /// <remarks>It also releases the DNS queries' destinations kept for their answers.</remarks>
    protected override void DisposeManaged()
    {
        _lastDnsServersV4.Dispose();
        _lastDnsServersV6.Dispose();

        base.DisposeManaged();
    }

    /// <summary>
    /// Runs <see cref="VpnHood.Net.PacketTransports.PacketTransportBase.DisposeUnmanaged"/> for an adapter that was not
    /// disposed. This adapter has no unmanaged resource of its own: the finalizer does not close the capture, so stop
    /// or dispose the adapter.
    /// </summary>
    /// <remarks>
    /// The adapter's handlers on the host's network-change events keep it reachable until it is disposed, so dispose it
    /// rather than count on this.
    /// </remarks>
    ~WinDivertVpnAdapter()
    {
        Dispose(false);
    }
}