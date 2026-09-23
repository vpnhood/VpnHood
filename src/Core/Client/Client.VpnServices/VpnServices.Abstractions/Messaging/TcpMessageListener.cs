using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

// A loopback-TCP IMessageListener. It binds a loopback TCP listener, generates an API key
// and publishes both into the shared config folder (vpn.api.json) so the app-side
// TcpMessageClient can discover and authenticate against it.
public sealed class TcpMessageListener : IMessageListener
{
    private const int MaxConsecutiveAcceptErrors = 5;
    private static readonly TimeSpan AcceptErrorDelay = TimeSpan.FromMilliseconds(100);

    private readonly string _apiFilePath;
    private readonly TcpListener _tcpListener;
    private readonly byte[] _apiKey;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private MessageHandler? _messageHandler;
    private int _disposed;

    public IPEndPoint ApiEndPoint { get; private set; }
    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public TcpMessageListener(string configFolder)
    {
        _apiFilePath = TcpMessageTransport.GetApiFilePath(configFolder);
        _apiKey = VhUtils.GenerateKey(keySizeInBit: 128);

        // bind immediately so the endpoint is available right away
        _tcpListener = new TcpListener(IPAddress.Loopback, 0);
        ApiEndPoint = Bind();

        VhLogger.Instance.LogDebug("TcpMessageListener has been created. EndPoint: {EndPoint}", ApiEndPoint);
    }

    private IPEndPoint Bind()
    {
        _tcpListener.Start();
        try {
            var apiEndPoint = (IPEndPoint)_tcpListener.LocalEndpoint;
            WriteBootstrapFile(apiEndPoint);
            return apiEndPoint;
        }
        catch {
            // Never leave a listener running on an endpoint that was not published.
            _tcpListener.Stop();
            throw;
        }
    }

    private void WriteBootstrapFile(IPEndPoint apiEndPoint)
    {
        var bootstrap = new TcpApiBootstrap {
            ApiEndPoint = apiEndPoint,
            ApiKey = _apiKey
        };

        var json = JsonSerializer.Serialize(bootstrap);
        var tempPath = _apiFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _apiFilePath, overwrite: true);
    }

    public async Task Start(MessageHandler messageHandler, CancellationToken cancellationToken)
    {
        _messageHandler = messageHandler;
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        await AcceptLoop(linkedCts.Token).Vhc();
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        var errorCount = 0;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).Vhc();
                    errorCount = 0;
                    _ = ProcessClientAsync(client, cancellationToken);
                }
                catch (Exception ex) when (!IsDisposed && !cancellationToken.IsCancellationRequested) {
                    errorCount++;
                    VhLogger.Instance.LogDebug(ex,
                        "Could not accept an API connection. ErrorCount: {ErrorCount}", errorCount);

                    if (errorCount >= MaxConsecutiveAcceptErrors && Rebind())
                        errorCount = 0;

                    await Task.Delay(AcceptErrorDelay, cancellationToken).Vhc();
                }
            }
        }
        catch (Exception ex) {
            if (!IsDisposed && !cancellationToken.IsCancellationRequested)
                VhLogger.Instance.LogError(ex, "TcpMessageListener accept loop has stopped.");
        }
        finally {
            _tcpListener.Stop();
            VhLogger.Instance.LogDebug("TcpMessageListener has been stopped. EndPoint: {EndPoint}", ApiEndPoint);
        }
    }

    private bool Rebind()
    {
        try {
            if (IsDisposed)
                return false;

            _tcpListener.Stop();
            ApiEndPoint = Bind();

            if (IsDisposed) {
                _tcpListener.Stop();
                VhUtils.TryDeleteFile(_apiFilePath);
                return false;
            }

            VhLogger.Instance.LogWarning("TcpMessageListener has been rebound. EndPoint: {EndPoint}", ApiEndPoint);
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not rebind the TcpMessageListener.");
            return false;
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var clientEp = client.TryGetLocalEndPoint();
        try {
            client.NoDelay = true;
            await using var stream = client.GetStream();

            // read and verify the API key frame
            var apiKey = await TcpMessageTransport
                .ReadFrameAsync(stream, TcpMessageTransport.MaxMessageLength, cancellationToken).Vhc();
            if (!_apiKey.AsSpan().SequenceEqual(apiKey.Span))
                throw new UnauthorizedAccessException("Invalid API key.");

            // process requests until the connection is closed
            var handler = _messageHandler ?? throw new InvalidOperationException("Listener is not started.");
            while (!cancellationToken.IsCancellationRequested) {
                var request = await TcpMessageTransport
                    .ReadFrameAsync(stream, TcpMessageTransport.MaxMessageLength, cancellationToken).Vhc();
                var response = await handler(request, cancellationToken).Vhc();
                await TcpMessageTransport.WriteFrameAsync(stream, response, cancellationToken).Vhc();
            }
        }
        catch (Exception ex) when (!IsDisposed) {
            VhLogger.Instance.LogDebug(ex, "Could not handle API connection. ClientEp: {ClientEp}", clientEp);
        }
        finally {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _tcpListener.Stop();
        VhUtils.TryDeleteFile(_apiFilePath);
    }
}
