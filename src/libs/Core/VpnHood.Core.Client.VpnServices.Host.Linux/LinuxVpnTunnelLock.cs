using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host.Linux;

/// <summary>
/// Represents the machine's one tunnel on Linux: a lock file in a folder under <c>/run</c>, which every app built on
/// VpnHood shares, and beside it a socket on which the holder hears an app ask it to let go.
/// </summary>
/// <remarks>
/// It keeps the folder root's alone, so a plain user can reach neither: one already there, as another tool may have left
/// it, gets that mode too, and a link there is refused. flock ends with its process, as the holder's tun does, so a crash
/// leaves nothing in the next app's way.
/// </remarks>
// folderPath: the lock's folder; a test's own leaves the machine's tunnel alone
internal sealed class LinuxVpnTunnelLock(string folderPath) : IVpnTunnelLock
{
    private const int WouldBlock = 11;
    private static readonly TimeSpan AskInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LetGoTimeout = TimeSpan.FromSeconds(30);
    private readonly string _lockPath = Path.Combine(folderPath, "tunnel.lock");
    private readonly string _socketPath = Path.Combine(folderPath, "tunnel.sock");

    public LinuxVpnTunnelLock()
        : this("/run/vpnhood-tunnel")
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Where another app holds it, it asks that app to let go each tenth of a second, for 30 seconds in all, its asks'
    /// connects among them.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The process may not use the folder: it is not root.</exception>
    /// <exception cref="IOException">
    /// The folder is a link, or the lock file or the socket's file could not be used.
    /// </exception>
    /// <exception cref="System.Net.Sockets.SocketException">The socket could not be bound.</exception>
    /// <exception cref="TimeoutException">Another app held it for 30 seconds.</exception>
    public async Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        // a take already canceled takes nothing, a free lock either
        cancellationToken.ThrowIfCancellationRequested();

        // root's alone: only root's services ask, and nobody else may look in
        FileUtils.CreateOwnerOnlyDirectory(folderPath);
        var lockFile = await TakeLockFile(cancellationToken).Vhc();
        try {
            return LinuxVpnTunnelHold.Start(lockFile, _socketPath, holder);
        }
        catch {
            await lockFile.DisposeAsync().Vhc();
            throw;
        }
    }

    // The lock, asking its holder to let go meanwhile, again each tenth of a second. One deadline for it all: an ask's
    // connect has no limit of its own
    private async Task<FileStream> TakeLockFile(CancellationToken cancellationToken)
    {
        var lockFile = TryOpenLockFile();
        if (lockFile != null)
            return lockFile;

        VhLogger.Instance.LogInformation("Another VPN app holds the tunnel; asking it to let go...");
        using var timeoutCts = new CancellationTokenSource(LetGoTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try {
            while (lockFile == null) {
                await Ask(linkedCts.Token).Vhc();
                await Task.Delay(AskInterval, linkedCts.Token).Vhc();
                lockFile = TryOpenLockFile();
            }
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested &&
                                                    !cancellationToken.IsCancellationRequested) {
            throw new TimeoutException("Another VPN app holds the tunnel and did not let it go.", ex);
        }

        return lockFile;
    }

    // Held open with no sharing, which .NET makes an exclusive flock; null while another process holds it.
    // 0600, since flock takes a file opened only to read.
    private FileStream? TryOpenLockFile()
    {
        try {
            return new FileStream(_lockPath, new FileStreamOptions {
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
    private async Task Ask(CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cancellationToken).Vhc();
        }
        catch (SocketException ex) {
            VhLogger.Instance.LogDebug("The tunnel's holder took no ask: {SocketError}", ex.SocketErrorCode);
        }
    }
}
