using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Linux;

// The machine's one tunnel on Linux: a lock file in a folder under /run that only root may enter, which
// every app built on VpnHood shares, and beside it a socket on which the holder hears an app ask it to let
// go; a plain user can reach neither. flock ends with its process, as the holder's tun does, so a crash
// leaves nothing in the next app's way.
public sealed class LinuxVpnTunnelLock : IVpnTunnelLock
{
    private const string FolderPath = "/run/vpnhood-tunnel";
    private const int WouldBlock = 11;
    private static readonly string LockPath = Path.Combine(FolderPath, "tunnel.lock");
    private static readonly string SocketPath = Path.Combine(FolderPath, "tunnel.sock");
    private static readonly TimeSpan AskInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LetGoTimeout = TimeSpan.FromSeconds(30);

    public async Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        // root's alone: only root's services ask, and nobody else may look in
        Directory.CreateDirectory(FolderPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var lockFile = await TakeLockFile(cancellationToken).Vhc();
        try {
            return LinuxVpnTunnelHold.Start(lockFile, SocketPath, holder);
        }
        catch {
            await lockFile.DisposeAsync().Vhc();
            throw;
        }
    }

    // The lock, asking its holder to let go meanwhile, again each tenth of a second.
    private static async Task<FileStream> TakeLockFile(CancellationToken cancellationToken)
    {
        var lockFile = TryOpenLockFile();
        if (lockFile != null)
            return lockFile;

        VhLogger.Instance.LogInformation("Another VPN app holds the tunnel; asking it to let go...");
        var stopwatch = Stopwatch.StartNew();
        while (lockFile == null) {
            if (stopwatch.Elapsed > LetGoTimeout)
                throw new TimeoutException("Another VPN app holds the tunnel and did not let it go.");

            await Ask(cancellationToken).Vhc();
            await Task.Delay(AskInterval, cancellationToken).Vhc();
            lockFile = TryOpenLockFile();
        }

        return lockFile;
    }

    // Held open with no sharing, which .NET makes an exclusive flock; null while another process holds it.
    // 0600, since flock takes a file opened only to read.
    private static FileStream? TryOpenLockFile()
    {
        try {
            return new FileStream(LockPath, new FileStreamOptions {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
        }
        catch (IOException ex) when (ex.HResult == WouldBlock) {
            return null;
        }
    }

    // An ask is a connection. The holder may be between its lock and its socket, or gone, so one refused
    // is asked again.
    private static async Task Ask(CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cancellationToken).Vhc();
        }
        catch (SocketException ex) {
            VhLogger.Instance.LogDebug("The tunnel's holder took no ask: {SocketError}", ex.SocketErrorCode);
        }
    }
}
