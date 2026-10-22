using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The VPN host's control channel, a named pipe: an AppContainer's pipes must live under LOCAL, and
// loopback TCP does not reach it from the app.
public sealed class WindowsVpnPluginPipeListener : IMessageListener
{
    internal const string PipeLeafName = "VpnService";
    private const string PipeName = "LOCAL\\" + PipeLeafName;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public async Task Start(MessageHandler messageHandler, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        while (!linkedCts.IsCancellationRequested) {
            var pipe = CreatePipe();
            try {
                await pipe.WaitForConnectionAsync(linkedCts.Token).Vhc();
                _ = ProcessClient(pipe, messageHandler, linkedCts.Token);
            }
            catch {
                await pipe.DisposeAsync().Vhc();
                if (!linkedCts.IsCancellationRequested)
                    throw;
            }
        }
    }

    private static NamedPipeServerStream CreatePipe() => new(
        PipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    private static async Task ProcessClient(NamedPipeServerStream pipe, MessageHandler messageHandler,
        CancellationToken cancellationToken)
    {
        await using (pipe) {
            try {
                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested) {
                    var request = await TcpMessageTransport.ReadFrameAsync(
                        pipe, TcpMessageTransport.MaxMessageLength, cancellationToken).Vhc();
                    var response = await messageHandler(request, cancellationToken).Vhc();
                    await TcpMessageTransport.WriteFrameAsync(pipe, response, cancellationToken).Vhc();
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !pipe.IsConnected) {
                // A disconnected client is normal; the accept loop already has another instance ready.
            }
            catch (Exception ex) {
                // This task is fire-and-forget, so an escaping exception would vanish unobserved.
                VhLogger.Instance.LogError(ex, "Could not process a named pipe message.");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
