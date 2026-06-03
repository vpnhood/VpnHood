using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

// A loopback-TCP IMessageClient. It keeps a single persistent connection to the
// TcpMessageListener and transparently reconnects when the connection drops. The endpoint
// and API key are discovered from the bootstrap file written by the listener.
public sealed class TcpMessageClient(string configFolder) : IMessageClient
{
    private readonly string _apiFilePath = TcpMessageTransport.GetApiFilePath(configFolder);
    private readonly AsyncLock _sendLock = new();
    private TcpClient? _tcpClient;
    private bool _isDisposed;

    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        using var scopeLock = await _sendLock.LockAsync(cancellationToken).Vhc();

        // try to reuse the live connection
        var tcpClient = _tcpClient;
        if (tcpClient is { Connected: true }) {
            try {
                return await SendCore(tcpClient, request, cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex,
                    "Could not send message to VpnService Host. Reconnecting... EndPoint: {EndPoint}",
                    tcpClient.TryGetRemoteEndPoint());
                DisposeConnection();
            }
        }

        // (re)connect and send
        tcpClient = await Connect(cancellationToken).Vhc();
        _tcpClient = tcpClient;
        try {
            return await SendCore(tcpClient, request, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            DisposeConnection();
            throw new VpnServiceUnreachableException("VpnService is unreachable.", ex);
        }
    }

    private static async Task<Memory<byte>> SendCore(TcpClient tcpClient, Memory<byte> request,
        CancellationToken cancellationToken)
    {
        var stream = tcpClient.GetStream();
        await TcpMessageTransport.WriteFrameAsync(stream, request, cancellationToken).Vhc();
        return await TcpMessageTransport
            .ReadFrameAsync(stream, TcpMessageTransport.MaxMessageLength, cancellationToken).Vhc();
    }

    private async Task<TcpClient> Connect(CancellationToken cancellationToken)
    {
        var bootstrap = ReadBootstrap();

        VhLogger.Instance.LogDebug("Connecting to VpnService Host... EndPoint: {EndPoint}", bootstrap.ApiEndPoint);
        var tcpClient = new TcpClient { NoDelay = true };
        try {
            await tcpClient.ConnectAsync(bootstrap.ApiEndPoint, cancellationToken).Vhc();

            // authenticate with the API key frame
            await TcpMessageTransport.WriteFrameAsync(tcpClient.GetStream(), bootstrap.ApiKey, cancellationToken)
                .Vhc();

            VhLogger.Instance.LogDebug("Connected to VpnService Host. LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
                tcpClient.TryGetLocalEndPoint(), tcpClient.TryGetRemoteEndPoint());
            return tcpClient;
        }
        catch (Exception ex) {
            tcpClient.Dispose();
            throw new VpnServiceUnreachableException(
                $"VpnService is unreachable. EndPoint: {bootstrap.ApiEndPoint}", ex);
        }
    }

    private TcpApiBootstrap ReadBootstrap()
    {
        if (!File.Exists(_apiFilePath))
            throw new VpnServiceNotReadyException("VpnService API endpoint is not available yet.");

        try {
            var json = File.ReadAllText(_apiFilePath);
            var bootstrap = JsonSerializer.Deserialize(json, ApiTransportJsonContext.For<TcpApiBootstrap>());
            if (bootstrap == null)
                throw new VpnServiceNotReadyException("VpnService API endpoint could not be deserialized.");
            return bootstrap;
        }
        catch (VpnServiceNotReadyException) {
            throw;
        }
        catch (Exception ex) {
            throw new VpnServiceNotReadyException("VpnService API endpoint is not available yet.", ex);
        }
    }

    private void DisposeConnection()
    {
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        DisposeConnection();
    }
}
