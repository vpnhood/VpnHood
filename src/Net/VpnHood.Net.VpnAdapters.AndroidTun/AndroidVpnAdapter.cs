using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Android.Content;
using Android.Net;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.AndroidTun.AndroidNative;
using OsConstants = Android.Systems.OsConstants;

namespace VpnHood.Net.VpnAdapters.AndroidTun;

/// <summary>
/// Provides the VPN adapter for Android, on the TUN interface that Android's <see cref="VpnService"/> gives an app: it
/// describes the interface to a <see cref="VpnService.Builder"/>, establishes it, and reads and writes its packets
/// through libc on its file descriptor.
/// </summary>
/// <param name="vpnService">
/// The app's VPN service, which the user has allowed to run a VPN (<see cref="VpnService.Prepare(Context)"/>).
/// </param>
/// <param name="vpnAdapterSettings">The adapter's settings.</param>
/// <remarks>
/// The interface stays up when the device moves to other networks, which Android handles; from Android 10, it takes
/// its meteredness from the networks under it. It has app filters, no NAT and no metric, and keeps a socket out of the
/// tunnel with <see cref="VpnService.Protect(int)"/>. Its steps run at once, and take no notice of their cancellation
/// tokens.
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">
/// The settings' blocking timeout is negative or too long, or their queue capacity is not above zero.
/// </exception>
public class AndroidVpnAdapter(VpnService vpnService, AndroidVpnAdapterSettings vpnAdapterSettings)
    : TunVpnAdapter(vpnAdapterSettings)
{
    // Poll with a finite timeout so a thread parked in WaitForTun when AdapterClose runs wakes up
    // and re-checks its state, instead of sleeping on a closed fd until stray traffic arrives.
    private const int PollTimeoutMs = 1000;

    private ParcelFileDescriptor? _parcelFileDescriptor;
    private VpnService.Builder? _builder;
    private int _tunFd = -1; // -1 means closed; snapshot it under _readLock/_writeLock before syscalls

    // Pair each I/O syscall with these locks against AdapterClose: once a tun fd is closed, the OS
    // may reuse its number, so a syscall racing the close could hit an unrelated fd. Read and write
    // are each single-threaded, so both locks are uncontended except at close time.
    private readonly Lock _readLock = new();
    private readonly Lock _writeLock = new();
    private StructPollfd[]? _pollFdReads;
    private StructPollfd[]? _pollFdWrites;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>: Android moves the interface to the new network.</value>
    protected override bool RestartAfterNetworkAddressChanged => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public override bool IsNatSupported(IpVersion ipVersion) => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="true"/>.</value>
    public override bool IsAppFilterSupported => true;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>: a socket is protected with <see cref="VpnService.Protect(int)"/>.</value>
    protected override bool IsSocketProtectedByBind => false;

    /// <inheritdoc/>
    /// <remarks>
    /// It starts the description of the interface: a non-blocking <see cref="VpnService.Builder"/> that, from Android
    /// 10, takes its meteredness from the networks under it. The interface itself comes with <see cref="AdapterOpen"/>.
    /// </remarks>
    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        _builder = new VpnService.Builder(vpnService)
            .SetBlocking(false);

        // Android 10 and above
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            _builder.SetMetered(false);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It closes the interface, as <see cref="AdapterClose"/> does.</remarks>
    protected override void AdapterRemove()
    {
        AdapterClose();
    }

    /// <inheritdoc/>
    /// <remarks>It establishes the interface the builder describes, and sets its file descriptor non-blocking.</remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    /// <exception cref="Exception">
    /// Android did not establish the interface, as when the app is not prepared to run a VPN or its VPN was revoked.
    /// </exception>
    /// <exception cref="PInvokeException">The file descriptor's flags could not be read or set.</exception>
    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Establishing Android tun adapter...");
        ArgumentNullException.ThrowIfNull(_builder);
        _parcelFileDescriptor = _builder.Establish() ?? throw new Exception("Could not establish VpnService.");

        // Packet I/O goes through libc on the raw fd (see AndroidAPI); ParcelFileDescriptor keeps
        // owning the fd and AdapterClose invalidates _tunFd before closing it.
        _tunFd = _parcelFileDescriptor.Fd;
        SetNonBlocking(_tunFd);
        _pollFdReads = [
            new StructPollfd {
                Fd = _tunFd, Events = (short)OsConstants.Pollin
            }
        ];
        _pollFdWrites = [
            new StructPollfd {
                Fd = _tunFd, Events = (short)OsConstants.Pollout
            }
        ];

        VhLogger.Instance.LogDebug("Android tun adapter has been established.");
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private static void SetNonBlocking(int fd)
    {
        var flags = AndroidAPI.fcntl(fd, AndroidAPI.FGetfl, 0);
        if (flags < 0)
            throw new PInvokeException("Failed to read TUN fd flags.", Marshal.GetLastWin32Error());

        if ((flags & AndroidAPI.ONonblock) != 0)
            return;

        if (AndroidAPI.fcntl(fd, AndroidAPI.FSetfl, flags | AndroidAPI.ONonblock) < 0)
            throw new PInvokeException("Failed to set TUN fd to non-blocking mode.", Marshal.GetLastWin32Error());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It invalidates the file descriptor while no read or write is under way, then closes it, which ends the VPN. A
    /// failure to close is logged as an error, and the descriptor's Java object is disposed all the same.
    /// </remarks>
    protected override void AdapterClose()
    {
        if (_parcelFileDescriptor == null)
            return;

        // invalidate the fd under both I/O locks: no syscall is in flight when the fd closes, and
        // later syscalls see -1. Poll needs no lock; it only observes and self-heals via its timeout.
        lock (_readLock)
        lock (_writeLock) {
            _tunFd = -1;
            _pollFdReads = null;
            _pollFdWrites = null;
        }

        // the close ends the VPN, which the dispose alone does not; a failed close still releases the Java peer
        VhLogger.Instance.LogDebug("Closing tun ParcelFileDescriptor...");
        VhInvoker.TryInvoke("close the VPN's descriptor", LogLevel.Error, _parcelFileDescriptor.Close);
        _parcelFileDescriptor.Dispose();
        _parcelFileDescriptor = null;
    }

    /// <summary>
    /// Keeps a socket's traffic out of the tunnel with <see cref="VpnService.Protect(int)"/>. Call it before the socket
    /// connects.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <returns><see langword="true"/> if Android protected the socket; otherwise, <see langword="false"/>.</returns>
    public override bool ProtectSocket(Socket socket)
    {
        var ret = vpnService.Protect(socket.Handle.ToInt32());
        return ret;
    }

    /// <summary>
    /// Keeps a socket's traffic out of the tunnel, as <see cref="ProtectSocket(Socket)"/> does: the remote address does
    /// not matter. Call it before the socket connects.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <param name="ipAddress">Not used.</param>
    /// <returns><see langword="true"/> if Android protected the socket; otherwise, <see langword="false"/>.</returns>
    public override bool ProtectSocket(Socket socket, IPAddress ipAddress)
    {
        var ret = vpnService.Protect(socket.Handle.ToInt32());
        return ret;
    }

    /// <summary>
    /// Gets the DNS servers of the networks under the VPN, as .NET lists none on Android: those of every network that
    /// has the internet, is not a VPN, and that Android has validated.
    /// </summary>
    /// <returns>
    /// The servers, without repeats, in this adapter's order of their networks' transports: Ethernet, Wi-Fi, USB,
    /// satellite, Bluetooth, cellular, then the rest. A network whose link properties Android does not give is skipped,
    /// and link-local IPv6 servers are left out, as their interface's scope is lost here.
    /// </returns>
    /// <remarks>
    /// A network Android has not validated is left out: a captive portal's resolver may answer every name with the
    /// portal's address.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Android gave no connectivity service.</exception>
    public override IReadOnlyList<IPAddress> GetPrimaryAdapterDnsServers()
    {
        var connectivityManager = vpnService.GetSystemService(Context.ConnectivityService) as ConnectivityManager ??
                                  throw new InvalidOperationException("Android gave no connectivity service.");

        return connectivityManager.GetAllNetworks()
            .Where(network => IsInternetNetwork(connectivityManager.GetNetworkCapabilities(network)))
            .OrderBy(network => GetTransportRank(connectivityManager.GetNetworkCapabilities(network)))
            .SelectMany(network => connectivityManager.GetLinkProperties(network)?.DnsServers ?? [])
            .Select(x => x.GetAddress())
            .OfType<byte[]>()
            .Select(x => new IPAddress(x))
            .Where(x => !x.IsIPv6LinkLocal) // its interface's scope is lost here
            .Distinct()
            .ToArray();
    }

    private static bool IsInternetNetwork(NetworkCapabilities? capabilities)
    {
        return capabilities != null &&
               capabilities.HasCapability(NetCapability.Internet) &&
               capabilities.HasCapability(NetCapability.Validated) &&
               capabilities.HasCapability(NetCapability.NotVpn);
    }

    private static readonly TransportType[] PreferredTransports = [
        TransportType.Ethernet, TransportType.Wifi, TransportType.Usb, TransportType.Satellite,
        TransportType.Bluetooth, TransportType.Cellular
    ];

    private static int GetTransportRank(NetworkCapabilities? capabilities)
    {
        var rank = capabilities == null ? -1 : Array.FindIndex(PreferredTransports, capabilities.HasTransport);
        return rank < 0 ? PreferredTransports.Length : rank;
    }

    /// <inheritdoc/>
    /// <remarks>It adds the address to the builder, for <see cref="AdapterOpen"/>.</remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.AddAddress(ipNetwork.Prefix.ToString(), ipNetwork.PrefixLength);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It adds the route to the builder, for <see cref="AdapterOpen"/>.</remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.AddRoute(ipNetwork.Prefix.ToString(), ipNetwork.PrefixLength);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: Android has no NAT for an app.</exception>
    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Android does not support NAT.");
    }

    /// <inheritdoc/>
    /// <remarks>It names the session on the builder, which Android shows in the VPN's settings and notification.</remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.SetSession(sessionName);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: Android gives an app's VPN no metric.</remarks>
    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It sets the MTU on the builder, for both IP versions.</remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.SetMtu(mtu);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It adds the servers to the builder, for <see cref="AdapterOpen"/>.</remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        foreach (var dnsServer in dnsServers)
            _builder.AddDnsServer(dnsServer.ToString());

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <value>The app's package name.</value>
    /// <exception cref="Exception">Android gave the service no application context.</exception>
    protected override string AppPackageId =>
        vpnService.ApplicationContext?.PackageName ?? throw new Exception("Could not get the app PackageName!");

    /// <inheritdoc/>
    /// <remarks>
    /// It adds each app to the builder; one Android refuses, as an app that is not installed, is logged as an error and
    /// left out.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        ArgumentNullException.ThrowIfNull(_builder);
        foreach (var packageId in packageIds)
            try {
                _builder.AddAllowedApplication(packageId);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not add an allowed app. PackageId: {PackageId}", packageId);
            }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It adds each app to the builder; one Android refuses, as an app that is not installed, is logged as an error and
    /// left out.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The builder is not made: <see cref="AdapterAdd"/> has not run.</exception>
    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        ArgumentNullException.ThrowIfNull(_builder);
        foreach (var packageId in packageIds)
            try {
                _builder.AddDisallowedApplication(packageId);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not add an disallowed app. PackageId: {PackageId}", packageId);
            }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It polls the file descriptor for up to a second at a time, so a reader whose adapter closed meanwhile wakes to
    /// see it, and polls again when a signal interrupts it. It returns at once when the adapter was closed before the
    /// wait began.
    /// </remarks>
    /// <exception cref="PInvokeException">The poll failed.</exception>
    protected override void WaitForTunRead()
    {
        var pollFds = _pollFdReads;
        if (pollFds != null)
            WaitForTun(pollFds);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It polls the file descriptor for up to a second at a time, so a writer whose adapter closed meanwhile wakes to
    /// see it, and polls again when a signal interrupts it. It returns at once when the adapter was closed before the
    /// wait began.
    /// </remarks>
    /// <exception cref="PInvokeException">The poll failed.</exception>
    protected override void WaitForTunWrite()
    {
        var pollFds = _pollFdWrites;
        if (pollFds != null)
            WaitForTun(pollFds);
    }

    private static void WaitForTun(StructPollfd[] pollFds)
    {
        while (true) {
            // a result of 0 (timeout) is fine; the caller re-checks its state and retries
            var result = AndroidAPI.poll(pollFds, (nuint)pollFds.Length, PollTimeoutMs);
            if (result >= 0)
                break; // Success, exit loop

            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == OsConstants.Eintr)
                continue; // Poll was interrupted, retry

            throw new PInvokeException("Failed to poll the TUN device for new data.", errorCode);
        }
    }

    /// <inheritdoc/>
    /// <returns>
    /// <see langword="true"/> if the packet was written; <see langword="false"/> if the interface's buffer is full.
    /// </returns>
    /// <exception cref="InvalidOperationException">The adapter is not open.</exception>
    /// <exception cref="IOException">The interface took only part of the packet.</exception>
    /// <exception cref="PInvokeException">The write failed.</exception>
    protected override bool WritePacket(IpPacket ipPacket)
    {
        var buffer = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var offset, out var length);

        lock (_writeLock) {
            var tunFd = _tunFd;
            if (tunFd < 0)
                throw new InvalidOperationException("Adapter is not open.");

            // TUN is packet-oriented: write() accepts the whole packet or fails, so no partial-write loop
            while (true) {
                var bytesWritten = AndroidAPI.write(tunFd, ref buffer[offset], (nuint)length);
                if (bytesWritten == length)
                    return true;

                if (bytesWritten >= 0)
                    throw new IOException($"Partial write to TUN device. Expected: {length}, Written: {bytesWritten}");

                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == OsConstants.Eintr)
                    continue; // interrupted before anything was written, retry immediately

                if (errorCode == OsConstants.Eagain)
                    return false; // buffer full; the caller backs off via WaitForTunWrite and retries

                throw new PInvokeException("Could not write to TUN.", errorCode);
            }
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The adapter is not open.</exception>
    /// <exception cref="IOException">The read returned no bytes, as when the interface has closed.</exception>
    /// <exception cref="PInvokeException">The read failed.</exception>
    protected override int ReadPacket(byte[] buffer)
    {
        lock (_readLock) {
            var tunFd = _tunFd;
            if (tunFd < 0)
                throw new InvalidOperationException("Adapter is not open.");

            while (true) {
                var bytesRead = AndroidAPI.read(tunFd, buffer, (nuint)buffer.Length);
                if (bytesRead > 0)
                    return (int)bytesRead;

                // 0 is EOF: the TUN fd has been closed
                if (bytesRead == 0)
                    throw new IOException("Could not read from TUN. The device has been closed.");

                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == OsConstants.Eintr)
                    continue; // interrupted before any data arrived, retry immediately

                if (errorCode == OsConstants.Eagain)
                    return 0; // no packet available; the caller waits via WaitForTunRead and retries

                throw new PInvokeException("Could not read from TUN.", errorCode);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>It closes an interface still open.</remarks>
    // No finalizer here: ParcelFileDescriptor has its own, and Java peers must not be touched from a .NET finalizer
    // because they may already be collected.
    protected override void DisposeUnmanaged()
    {
        if (_parcelFileDescriptor != null)
            AdapterRemove();

        base.DisposeUnmanaged();
    }
}
