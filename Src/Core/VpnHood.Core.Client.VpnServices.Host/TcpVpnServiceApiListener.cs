using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Client.VpnServices.Host;

// Default VpnService API transport: a loopback TCP listener with an API key handshake.
public sealed class TcpVpnServiceApiListener : IVpnServiceApiListener
{
    private int _isDisposed;
    private readonly TcpListener _tcpListener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private IVpnServiceApiRequestHandler? _requestHandler;

    public IPEndPoint? ApiEndPoint { get; private set; }
    public byte[]? ApiKey { get; }

    public TcpVpnServiceApiListener(int port = 0, byte[]? apiKey = null)
    {
        ApiKey = apiKey ?? VhUtils.GenerateKey(keySizeInBit: 128);
        _tcpListener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start(IVpnServiceApiRequestHandler requestHandler)
    {
        _requestHandler = requestHandler;
        _ = AcceptLoop(_cancellationTokenSource.Token);
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        try {
            _tcpListener.Start();
            ApiEndPoint = (IPEndPoint)_tcpListener.LocalEndpoint;
            VhLogger.Instance.LogDebug("VpnService ApiListener has started. EndPoint: {EndPoint}", ApiEndPoint);

            while (!cancellationToken.IsCancellationRequested) {
                var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                _ = ProcessClientAsync(client, cancellationToken);
            }
        }
        catch (Exception ex) {
            if (_isDisposed == 0)
                VhLogger.Instance.LogError(ex, "VpnService ApiListener has stopped.");
        }
        finally {
            _tcpListener.Stop();
            VhLogger.Instance.LogDebug("VpnService ApiListener has been stopped. EndPoint: {EndPoint}", ApiEndPoint);
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var requestHandler = _requestHandler ?? throw new InvalidOperationException("Listener has not been started.");
        var clientEp = client.TryGetLocalEndPoint();
        try {
            client.NoDelay = true;
            await using var stream = client.GetStream();

            // read api key and compare
            var apiKey = await StreamUtils.ReadObjectAsync(stream, ApiTransportJsonContext.For<byte[]>(),
                cancellationToken);
            if (ApiKey == null || !ApiKey.SequenceEqual(apiKey))
                throw new Exception("Invalid API key.");

            while (!cancellationToken.IsCancellationRequested)
                await requestHandler.ProcessRequestAsync(stream, stream, cancellationToken);
        }
        catch (Exception ex) {
            if (_isDisposed == 0)
                VhLogger.Instance.LogError(GeneralEventId.Test, ex,
                    "Could not handle API request. ClientEp: {ClientEp}", clientEp);
        }
        finally {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        _cancellationTokenSource.Cancel();
        _tcpListener.Stop();
    }
}
