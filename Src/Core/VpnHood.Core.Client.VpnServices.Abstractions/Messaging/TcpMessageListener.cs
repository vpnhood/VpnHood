using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

// A loopback-TCP IMessageListener. It binds a loopback TCP listener, generates an API key
// and publishes both into the shared config folder (vpn.api.json) so the app-side
// TcpMessageClient can discover and authenticate against it.
public sealed class TcpMessageListener : IMessageListener
{
    private readonly string _apiFilePath;
    private readonly TcpListener _tcpListener;
    private readonly byte[] _apiKey;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private MessageHandler? _messageHandler;
    private bool _disposed;

    public IPEndPoint ApiEndPoint { get; }

    public TcpMessageListener(string configFolder)
    {
        _apiFilePath = TcpMessageTransport.GetApiFilePath(configFolder);
        _apiKey = VhUtils.GenerateKey(keySizeInBit: 128);

        // bind immediately so the endpoint is available right away
        _tcpListener = new TcpListener(IPAddress.Loopback, 0);
        _tcpListener.Start();
        ApiEndPoint = (IPEndPoint)_tcpListener.LocalEndpoint;

        // publish the bootstrap info so the client can discover and authenticate
        WriteBootstrapFile();

        VhLogger.Instance.LogDebug("TcpMessageListener has been created. EndPoint: {EndPoint}", ApiEndPoint);
    }

    private void WriteBootstrapFile()
    {
        var bootstrap = new TcpApiBootstrap {
            ApiEndPoint = ApiEndPoint,
            ApiKey = _apiKey
        };
        var json = JsonSerializer.Serialize(bootstrap);
        File.WriteAllText(_apiFilePath, json);
    }

    public Task Start(MessageHandler messageHandler, CancellationToken cancellationToken)
    {
        _messageHandler = messageHandler;
        return AcceptLoop(_cancellationTokenSource.Token);
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).Vhc();
                _ = ProcessClientAsync(client, cancellationToken);
            }
        }
        catch (Exception ex) {
            if (!_disposed)
                VhLogger.Instance.LogError(ex, "TcpMessageListener accept loop has stopped.");
        }
        finally {
            _tcpListener.Stop();
            VhLogger.Instance.LogDebug("TcpMessageListener has been stopped. EndPoint: {EndPoint}", ApiEndPoint);
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
        catch (Exception ex) when (!_disposed) {
            VhLogger.Instance.LogDebug(ex, "Could not handle API connection. ClientEp: {ClientEp}", clientEp);
        }
        finally {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _tcpListener.Stop();
        VhUtils.TryDeleteFile(_apiFilePath);
    }
}
