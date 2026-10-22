using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;
using Windows.ApplicationModel;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The app's end of the VPN host's control channel. The app runs at medium IL, outside the VPN host's
// AppContainer, so it opens the pipe in that container's namespace: from here, LOCAL\ resolves in the wrong
// namespace and the connect times out.
public sealed class WindowsVpnPluginPipeClient : IMessageClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly Lazy<string> DesktopPipeName = new(BuildDesktopPipeName, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly AsyncLock _sendLock = new();
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scopeLock = await _sendLock.LockAsync(cancellationToken).Vhc();

        if (_pipe is { IsConnected: true } pipe) {
            try {
                return await SendCore(pipe, request, cancellationToken).Vhc();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch {
                DisposePipe();
            }
        }

        pipe = await Connect(cancellationToken).Vhc();
        _pipe = pipe;
        try {
            return await SendCore(pipe, request, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            DisposePipe();
            throw new VpnServiceUnreachableException("The Windows VPN plug-in's service is unreachable.", ex);
        }
    }

    private static async Task<Memory<byte>> SendCore(Stream pipe, Memory<byte> request,
        CancellationToken cancellationToken)
    {
        await TcpMessageTransport.WriteFrameAsync(pipe, request, cancellationToken).Vhc();
        return await TcpMessageTransport.ReadFrameAsync(pipe, TcpMessageTransport.MaxMessageLength, cancellationToken).Vhc();
    }

    private static async Task<NamedPipeClientStream> Connect(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", DesktopPipeName.Value, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try {
            await pipe.ConnectAsync(linkedCts.Token).Vhc();
            return pipe;
        }
        catch (Exception ex) {
            await pipe.DisposeAsync().Vhc();
            cancellationToken.ThrowIfCancellationRequested();
            throw new VpnServiceUnreachableException("The Windows VPN plug-in's service pipe is unavailable.", ex);
        }
    }

    // Windows derives a package's AppContainer SID from the SHA-256 of its lower-case family name: the first
    // seven little-endian UInt32 values are the S-1-15-2 sub-authorities. Computed here, once per process:
    // marshalling the SID that DeriveAppContainerSidFromAppContainerName returns failed now and then (access
    // violations, Win32 error 87), leaving the UI's status and speed stale while the tunnel carried traffic.
    private static string BuildDesktopPipeName()
    {
        var appContainerName = Package.Current.Id.FamilyName.ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(appContainerName));
        var subAuthorities = new string[7];
        for (var i = 0; i < subAuthorities.Length; i++)
            subAuthorities[i] = BitConverter.ToUInt32(hash, i * sizeof(uint)).ToString(CultureInfo.InvariantCulture);

        var sid = $"S-1-15-2-{string.Join('-', subAuthorities)}";
        using var process = Process.GetCurrentProcess();
        return $"Sessions\\{process.SessionId}\\AppContainerNamedObjects\\{sid}\\{WindowsVpnPluginPipeListener.PipeLeafName}";
    }

    private void DisposePipe()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposePipe();
    }
}
