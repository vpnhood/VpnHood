using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Android.Net;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.AndroidTun.AndroidNative;
using OsConstants = Android.Systems.OsConstants;

namespace VpnHood.Core.VpnAdapters.AndroidTun;

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

    protected override bool RestartAfterNetworkAddressChanged => false;
    public override bool IsNatSupported => false;
    public override bool IsAppFilterSupported => true;
    protected override bool IsSocketProtectedByBind => false;

    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        _builder = new VpnService.Builder(vpnService)
            .SetBlocking(false);

        // Android 10 and above
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            _builder.SetMetered(false);

        return Task.CompletedTask;
    }

    protected override void AdapterRemove()
    {
        AdapterClose();
    }

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

        try {
            VhLogger.Instance.LogDebug("Closing tun ParcelFileDescriptor...");
            _parcelFileDescriptor.Close(); //required to close the vpn. dispose is not enough
            _parcelFileDescriptor.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService.");
        }
        finally {
            _parcelFileDescriptor = null;
        }
    }

    public override bool ProtectSocket(Socket socket)
    {
        var ret = vpnService.Protect(socket.Handle.ToInt32());
        return ret;
    }

    public override bool ProtectSocket(Socket socket, IPAddress ipAddress)
    {
        var ret = vpnService.Protect(socket.Handle.ToInt32());
        return ret;
    }

    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.AddAddress(ipNetwork.Prefix.ToString(), ipNetwork.PrefixLength);
        return Task.CompletedTask;
    }

    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.AddRoute(ipNetwork.Prefix.ToString(), ipNetwork.PrefixLength);
        return Task.CompletedTask;
    }

    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Android does not support NAT.");
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.SetSession(sessionName);
        return Task.CompletedTask;
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        // ignore metric
        return Task.CompletedTask;
    }

    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        _builder.SetMtu(mtu);
        return Task.CompletedTask;
    }

    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        foreach (var dnsServer in dnsServers)
            _builder.AddDnsServer(dnsServer.ToString());

        return Task.CompletedTask;
    }

    protected override string AppPackageId =>
        vpnService.ApplicationContext?.PackageName ?? throw new Exception("Could not get the app PackageName!");

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

    protected override void WaitForTunRead()
    {
        var pollFds = _pollFdReads;
        if (pollFds != null)
            WaitForTun(pollFds);
    }

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

    protected override bool ReadPacket(byte[] buffer)
    {
        lock (_readLock) {
            var tunFd = _tunFd;
            if (tunFd < 0)
                throw new InvalidOperationException("Adapter is not open.");

            while (true) {
                var bytesRead = AndroidAPI.read(tunFd, buffer, (nuint)buffer.Length);
                if (bytesRead > 0)
                    return true;

                // 0 is EOF: the TUN fd has been closed
                if (bytesRead == 0)
                    throw new IOException("Could not read from TUN. The device has been closed.");

                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == OsConstants.Eintr)
                    continue; // interrupted before any data arrived, retry immediately

                if (errorCode == OsConstants.Eagain)
                    return false; // no packet available; the caller waits via WaitForTunRead and retries

                throw new PInvokeException("Could not read from TUN.", errorCode);
            }
        }
    }

    protected override void DisposeUnmanaged()
    {
        // The adapter is an unmanaged resource; it must be closed if it is open.
        // No finalizer here: ParcelFileDescriptor has its own, and Java peers must not be touched
        // from a .NET finalizer because they may already be collected.
        if (_parcelFileDescriptor != null)
            AdapterRemove();

        base.DisposeUnmanaged();
    }
}
