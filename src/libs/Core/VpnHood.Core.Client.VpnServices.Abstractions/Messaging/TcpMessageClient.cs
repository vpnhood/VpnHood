using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

/// <summary>
/// Represents the app's side of the loopback TCP channel to the VPN service: one connection to
/// <see cref="TcpMessageListener"/>, kept, and made again when it drops, to the endpoint and with the key the listener
/// published in the config folder.
/// </summary>
/// <param name="configFolder">The VPN service's config folder, where the listener publishes its endpoint and key.</param>
public sealed class TcpMessageClient(string configFolder) : IMessageClient
{
    private readonly string _apiFilePath = TcpMessageTransport.GetApiFilePath(configFolder);
    private readonly AsyncLock _sendLock = new();
    // the connection and the close, one at a time: a send that connected puts no connection in place after the close
    private readonly Lock _connectionLock = new();
    private TcpClient? _tcpClient;
    private bool _disposed;

    // how long a connect may take; a test shortens it
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    /// <remarks>
    /// A request that could not be written on the kept connection is sent on a new one; one written is never sent again,
    /// as the service may have taken it, so a failure before its answer throws. A connect may take 5 seconds.
    /// </remarks>
    /// <exception cref="VpnServiceNotReadyException">The listener's file is missing or cannot be read.</exception>
    /// <exception cref="VpnServiceUnreachableException">
    /// The service cannot be reached, with a <see cref="TimeoutException"/> inside where it took no connection in 5
    /// seconds, or the connection failed before its answer; an answer that never comes is waited for until the token is
    /// canceled.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The client is disposed, before or while the send waited its turn.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scopeLock = await _sendLock.LockAsync(cancellationToken).Vhc();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_tcpClient != null && !IsReusable(_tcpClient))
            DisposeConnection();

        var tcpClient = _tcpClient;
        if (tcpClient != null) {
            var isWritten = false;
            try {
                var stream = tcpClient.GetStream();
                await TcpMessageTransport.WriteFrameAsync(stream, request, cancellationToken).Vhc();
                isWritten = true;
                return await TcpMessageTransport
                    .ReadFrameAsync(stream, TcpMessageTransport.MaxMessageLength, cancellationToken).Vhc();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                DisposeConnection();
                throw;
            }
            catch (Exception ex) when (!isWritten) {
                VhLogger.Instance.LogDebug(ex,
                    "Could not send message to VpnService Host. Reconnecting... EndPoint: {EndPoint}",
                    tcpClient.TryGetRemoteEndPoint());
                DisposeConnection();
            }
            catch (Exception ex) {
                DisposeConnection();
                throw new VpnServiceUnreachableException("VpnService connection failed before its answer.", ex);
            }
        }

        tcpClient = await Connect(cancellationToken).Vhc();
        SetConnection(tcpClient);
        try {
            return await SendCore(tcpClient, request, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            DisposeConnection();
            throw;
        }
        catch (Exception ex) {
            DisposeConnection();
            throw new VpnServiceUnreachableException("VpnService is unreachable.", ex);
        }
    }

    // the new connection in place, or closed where the client was closed meanwhile
    private void SetConnection(TcpClient tcpClient)
    {
        lock (_connectionLock) {
            if (!_disposed) {
                _tcpClient = tcpClient;
                return;
            }
        }

        tcpClient.Dispose();
        throw new ObjectDisposedException(GetType().FullName);
    }

    private static bool IsReusable(TcpClient tcpClient)
    {
        try {
            // No data is expected between requests. A readable idle socket is closed or out of sync.
            return tcpClient.Connected && !tcpClient.Client.Poll(0, SelectMode.SelectRead);
        }
        catch {
            return false;
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
        cancellationToken.ThrowIfCancellationRequested();
        var bootstrap = ReadBootstrap();

        VhLogger.Instance.LogDebug("Connecting to VpnService Host... EndPoint: {EndPoint}", bootstrap.ApiEndPoint);
        using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var tcpClient = new TcpClient { NoDelay = true };
        try {
            await tcpClient.ConnectAsync(bootstrap.ApiEndPoint, linkedCts.Token).Vhc();

            // authenticate with the API key frame
            await TcpMessageTransport.WriteFrameAsync(tcpClient.GetStream(), bootstrap.ApiKey, linkedCts.Token)
                .Vhc();

            VhLogger.Instance.LogDebug("Connected to VpnService Host. LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
                tcpClient.TryGetLocalEndPoint(), tcpClient.TryGetRemoteEndPoint());
            return tcpClient;
        }
        catch (Exception ex) {
            tcpClient.Dispose();

            // the connect's own deadline is a timeout, which a caller tells from a cut connection, as for a request
            cancellationToken.ThrowIfCancellationRequested();
            if (timeoutCts.IsCancellationRequested)
                throw new VpnServiceUnreachableException(
                    $"VpnService took no connection in {ConnectTimeout.TotalSeconds} seconds. " +
                    $"EndPoint: {bootstrap.ApiEndPoint}", new TimeoutException(ex.Message, ex));

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
            var bootstrap = JsonSerializer.Deserialize<TcpApiBootstrap>(json);
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
        lock (_connectionLock) {
            _tcpClient?.Dispose();
            _tcpClient = null;
        }
    }

    /// <summary>Closes the connection. A send that waits for its turn then throws, and puts no connection in place.</summary>
    public void Dispose()
    {
        lock (_connectionLock) {
            if (_disposed)
                return;

            _disposed = true;
        }

        DisposeConnection();
    }
}
