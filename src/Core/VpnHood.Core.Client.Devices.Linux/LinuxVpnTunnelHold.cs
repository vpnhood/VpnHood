using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.Devices.Linux;

// One app's hold on the tunnel (LinuxVpnTunnelLock): the lock file held open, and the socket on which an
// app's ask reaches the holder, once. The hold ends as soon as the holder returns from that ask, its
// adapter gone, or when it is disposed. Its end takes the socket away before it lets the lock go, so the
// next holder finds none.
internal sealed class LinuxVpnTunnelHold : IDisposable
{
    private readonly FileStream _lockFile;
    private readonly Socket _listener;
    private readonly string _socketPath;
    private readonly IVpnTunnelHolder _holder;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    // an ask's call to the holder and the end that follows it are one step: a Dispose meanwhile waits
    private readonly Lock _endLock = new();
    private bool _isEnded;

    private LinuxVpnTunnelHold(FileStream lockFile, Socket listener, string socketPath, IVpnTunnelHolder holder)
    {
        _lockFile = lockFile;
        _listener = listener;
        _socketPath = socketPath;
        _holder = holder;
    }

    public static LinuxVpnTunnelHold Start(FileStream lockFile, string socketPath, IVpnTunnelHolder holder)
    {
        // one a crashed holder left would refuse the bind
        File.Delete(socketPath);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen();
        }
        catch {
            listener.Dispose();
            throw;
        }

        var hold = new LinuxVpnTunnelHold(lockFile, listener, socketPath, holder);
        _ = hold.Listen();
        return hold;
    }

    private async Task Listen()
    {
        while (!_cancellationTokenSource.IsCancellationRequested) {
            try {
                using var caller = await _listener.AcceptAsync(_cancellationTokenSource.Token).Vhc();
            }
            catch (OperationCanceledException) {
                return;
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (SocketException ex) {
                VhLogger.Instance.LogError(ex, "The tunnel's socket failed, so no other app can ask for the tunnel.");
                return;
            }

            OnAsked();
        }
    }

    // The holder hears of the ask, its adapter gone once it returns, and the lock goes right after.
    private void OnAsked()
    {
        lock (_endLock) {
            if (_isEnded)
                return;

            try {
                _holder.OnTunnelRevoked();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not tell the tunnel's holder that another app asked for it.");
            }

            End();
        }
    }

    public void Dispose()
    {
        lock (_endLock)
            End();
    }

    private void End()
    {
        if (_isEnded)
            return;

        _isEnded = true;
        _cancellationTokenSource.TryCancel();
        _listener.Dispose();
        VhInvoker.TryInvoke("remove the tunnel's socket", () => File.Delete(_socketPath));

        // the flock goes last
        _lockFile.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
