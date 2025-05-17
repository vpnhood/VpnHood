using System.Net;
using System.Runtime.InteropServices;
using Android.Net;
using Android.OS;
using Android.Systems;
using Java.IO;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.VpnAdapters.AndroidTun;

public class AndroidVpnAdapter(VpnService vpnService, AndroidVpnAdapterSettings vpnAdapterSettings)
    : TunVpnAdapter(vpnAdapterSettings)
{
    private ParcelFileDescriptor? _parcelFileDescriptor;
    private VpnService.Builder? _builder;
    private FileInputStream? _inStream;
    private FileOutputStream? _outStream;
    private StructPollfd[]? _pollFdReads;
    private StructPollfd[]? _pollFdWrites;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];
    private readonly byte[] _readBuffer = new byte[0xFFFF];

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
        _pollFdReads = [new StructPollfd {
            Fd = _parcelFileDescriptor.FileDescriptor, Events = (short)OsConstants.Pollin
        }];
        _pollFdWrites = [new StructPollfd {
            Fd = _parcelFileDescriptor.FileDescriptor, Events = (short)OsConstants.Pollout }];

        //Packets to be sent are queued in this input stream.
        _inStream = new FileInputStream(_parcelFileDescriptor.FileDescriptor);

        //Packets received need to be written to this output stream.
        _outStream = new FileOutputStream(_parcelFileDescriptor.FileDescriptor);

        VhLogger.Instance.LogDebug("Android tun adapter has been established.");
        return Task.CompletedTask;
    }

    protected override void AdapterClose()
    {
        if (_parcelFileDescriptor == null)
            return;

        // close in streams
        try {
            VhLogger.Instance.LogDebug("Closing tun in stream...");
            _inStream?.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService streams.");
        }

        // close out streams
        try {
            VhLogger.Instance.LogDebug("Closing tun out stream...");
            _outStream?.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService streams.");
        }

        // close the vpn
        try {
            VhLogger.Instance.LogDebug("Closing tun ParcelFileDescriptor...");
            _parcelFileDescriptor?.Close(); //required to close the vpn. dispose is not enough
            _parcelFileDescriptor?.Dispose();
            _parcelFileDescriptor = null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService.");
        }
    }

    public override bool ProtectSocket(System.Net.Sockets.Socket socket)
    {
        return vpnService.Protect(socket.Handle.ToInt32());
    }

    public override bool ProtectSocket(System.Net.Sockets.Socket socket, IPAddress ipAddress)
    {
        return vpnService.Protect(socket.Handle.ToInt32());
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

    protected override Task SetDnsServers(IPAddress[] dnsServers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_builder);
        foreach (var dnsServer in dnsServers)
            _builder.AddDnsServer(dnsServer.ToString());

        return Task.CompletedTask;
    }

    protected override string AppPackageId =>
        vpnService.ApplicationContext?.PackageName ?? throw new Exception("Could not get the app PackageName!");

    protected override Task SetAllowedApps(string[] packageIds, CancellationToken cancellationToken)
    {
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

    protected override Task SetDisallowedApps(string[] packageIds, CancellationToken cancellationToken)
    {
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
        if (_pollFdReads != null)
            WaitForTun(_pollFdReads);
    }
    protected override void WaitForTunWrite()
    {
        if (_pollFdWrites != null)
            WaitForTun(_pollFdWrites);
    }

    private static void WaitForTun(StructPollfd[] pollFds)
    {
        while (true) {
            var result = Os.Poll(pollFds, -1);
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
        if (_outStream == null)
            throw new InvalidOperationException("Adapter is not open.");

        ipPacket.Buffer.Span.CopyTo(_writeBuffer);
        _outStream.Write(_writeBuffer, 0, ipPacket.Buffer.Length);
        return true;
    }

    protected override IpPacket? ReadPacket(int mtu)
    {
        if (_inStream == null)
            throw new InvalidOperationException("Adapter is not open.");

        var bytesRead = _inStream.Read(_readBuffer);
        return bytesRead switch {
            // no more packet
            0 => null,
            // Parse the packet and add to the list
            > 0 => PacketBuilder.Parse(_readBuffer.AsSpan(0, bytesRead)),
            // error
            < 0 => throw new System.IO.IOException("Could not read from TUN.")
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // The adapter is an unmanaged resource; it must be closed if it is open
        if (_parcelFileDescriptor != null)
            AdapterRemove();
    }

    ~AndroidVpnAdapter()
    {
        Dispose(false);
    }
}
