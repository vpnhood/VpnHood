using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorServiceBase : IDisposable
{
    private const bool UseBuffer = true;
    private readonly ISocketFactory _socketFactory;
    private readonly ConcurrentQueue<ClientStreamItem> _freeClientStreams = new();
    private readonly HashSet<IClientStream> _sharedClientStream = new(50); // for preventing reuse
    private readonly Job _cleanupJob;
    private int _isDisposed;
    private bool _useWebSocket;
    private readonly ProxyEndPointManager _proxyEndPointManager;

    public ClientConnectorStatus Status { get; }
    public TimeSpan TcpReuseTimeout { get; private set; }
    public int ProtocolVersion { get; private set; } = 8;
    public VpnEndPoint VpnEndPoint { get; init; }

    protected ConnectorServiceBase(ConnectorServiceOptions options)
    {
        _socketFactory = options.SocketFactory;
        AllowTcpReuse = options.AllowTcpReuse;
        _proxyEndPointManager = options.ProxyEndPointManager;
        Status = new ClientConnectorStatusImpl(this);
        TcpReuseTimeout = TimeSpan.FromSeconds(30).WhenNoDebugger();
        VpnEndPoint = options.VpnEndPoint;
        _cleanupJob = new Job(Cleanup, "ConnectorCleanup");
    }

    protected void Init(int protocolVersion, byte[]? serverSecret, TimeSpan tcpReuseTimeout, bool useWebSocket)
    {
        _ = serverSecret;
        ProtocolVersion = protocolVersion;
        TcpReuseTimeout = tcpReuseTimeout;
        _useWebSocket = useWebSocket;
    }

    private static string BuildPostRequest(
        string hostName, TunnelStreamType streamType, int protocolVersion, int contentLength)
    {
        // write HTTP request
        var headerBuilder = new StringBuilder()
            .Append($"POST /{Guid.NewGuid()} HTTP/1.1\r\n")
            .Append($"Host: {hostName}\r\n")
            .Append($"Content-Length: {contentLength}\r\n")
            .Append("Content-Type: application/octet-stream\r\n")
            .Append("User-Agent: Hood\r\n")
            .Append($"X-Buffered: {UseBuffer}\r\n")
            .Append($"X-ProtocolVersion: {protocolVersion}\r\n")
            .Append($"X-BinaryStream: {streamType}\r\n")
            .Append("\r\n");

        var header = headerBuilder.ToString();
        return header;
    }

    private async Task<IClientStream> CreateWebSocketClientStream(TcpClient tcpClient, Stream sslStream,
        string streamId, CancellationToken cancellationToken)
    {
        var webSocketKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        // write HTTP request
        var headerBuilder = new StringBuilder()
            .Append($"GET /{Guid.NewGuid()} HTTP/1.1\r\n")
            .Append($"Host: {VpnEndPoint.HostName}\r\n")
            .Append($"X-Buffered: {UseBuffer}\r\n")
            .Append($"X-ProtocolVersion: {ProtocolVersion}\r\n")
            .Append("Upgrade: websocket\r\n")
            .Append("Connection: Upgrade\r\n")
            .Append("Sec-WebSocket-Version: 13\r\n")
            .Append($"Sec-WebSocket-Key: {webSocketKey}\r\n")
            .Append("\r\n");

        // Send header and wait for its response
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(headerBuilder.ToString()), cancellationToken).Vhc();

        // wait for response and websocket upgrade if needed before sending any data because GET can not have body
        var responseMessage = await HttpUtils.ReadResponse(sslStream, cancellationToken).Vhc();
        if (responseMessage.StatusCode != HttpStatusCode.SwitchingProtocols)
            throw new Exception("Unexpected response.");

        // create a client stream
        var webSocketStream = new WebSocketStream(sslStream, streamId, UseBuffer, isServer: false);
        var clientStream = new TcpClientStream(tcpClient, webSocketStream, streamId,
            AllowTcpReuse ? ClientStreamReuseCallback : null);
        clientStream.RequireHttpResponse = false;

        // If reuse is allowed, add the client stream to the shared collection
        if (AllowTcpReuse) {
            lock (_sharedClientStream)
                _sharedClientStream.Add(clientStream);
        }

        return clientStream;
    }

    private async Task<IClientStream> CreateSimpleClientStream(TcpClient tcpClient, Stream sslStream,
        int contentLength, string streamId, CancellationToken cancellationToken)
    {
        // write HTTP request
        var header = BuildPostRequest(VpnEndPoint.HostName, TunnelStreamType.None, ProtocolVersion, contentLength);

        // Send header and wait for its response
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken).Vhc();

        // create a client stream
        var clientStream = new TcpClientStream(tcpClient, sslStream, streamId);
        clientStream.RequireHttpResponse = true;
        return clientStream;
    }

    [Obsolete("Obsolete")]
    private async Task<IClientStream> CreateStandardClientStream(TcpClient tcpClient, Stream sslStream,
        int contentLength, string streamId, CancellationToken cancellationToken)
    {
        // write HTTP request
        var header = BuildPostRequest(VpnEndPoint.HostName, TunnelStreamType.Standard, ProtocolVersion,
            contentLength: contentLength);

        // Send header and wait for its response
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken).Vhc();

        // create a client stream
        var standardStream = new BinaryStreamStandard(sslStream, streamId, UseBuffer);
        var clientStream = new TcpClientStream(tcpClient, standardStream, streamId, ClientStreamReuseCallback);
        clientStream.RequireHttpResponse = true;
        return clientStream;
    }


    private async Task<IClientStream> CreateClientStream(string streamId, TcpClient tcpClient, Stream sslStream,
        int contentLength, CancellationToken cancellationToken)
    {
        // WebSocket
        if (_useWebSocket)
            return await CreateWebSocketClientStream(tcpClient, sslStream, streamId, cancellationToken);

#pragma warning disable CS0618 // Type or member is obsolete
        if (AllowTcpReuse)
            return await CreateStandardClientStream(tcpClient, sslStream, contentLength, streamId, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete

        // Simple
        return await CreateSimpleClientStream(tcpClient, sslStream, contentLength, streamId, cancellationToken);
    }

    protected async Task<IClientStream> GetTlsConnectionToServer(string streamId, int contentLength,
        CancellationToken cancellationToken)
    {
        var tcpEndPoint = VpnEndPoint.TcpEndPoint;

        // create new stream
        TcpClient? tcpClient = null;
        try {
            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "Establishing a new TCP to the Server... EndPoint: {EndPoint}", VhLogger.Format(tcpEndPoint));

            // Client.SessionTimeout does not affect in ConnectAsync
            if (_proxyEndPointManager.IsEnabled)
                tcpClient = await _proxyEndPointManager.ConnectAsync(tcpEndPoint, cancellationToken);
            else {
                tcpClient = _socketFactory.CreateTcpClient(tcpEndPoint);
                await tcpClient.ConnectAsync(tcpEndPoint, cancellationToken).Vhc();
            }

            return await GetTlsConnectionToServer(streamId, tcpClient, contentLength, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            // record failed proxy
            if (_proxyEndPointManager.IsEnabled && tcpClient != null)
                _proxyEndPointManager.RecordFailed(tcpClient, ex);

            tcpClient?.Dispose();
            throw;
        }
    }

    private async Task<IClientStream> GetTlsConnectionToServer(string streamId, TcpClient tcpClient,
        int contentLength, CancellationToken cancellationToken)
    {
        // Establish a TLS connection
        var sslStream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);
        try {
            var hostName = VpnEndPoint.HostName;
            VhLogger.Instance.LogDebug(GeneralEventId.Request, "TLS Authenticating... HostName: {HostName}",
                VhLogger.FormatHostName(hostName));

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = hostName,
                EnabledSslProtocols = SslProtocols.None // auto
            }, cancellationToken)
                .Vhc();

            var clientStream = await CreateClientStream(streamId, tcpClient, sslStream, contentLength, cancellationToken).Vhc();
            lock (Status) Status.CreatedConnectionCount++;
            return clientStream;
        }
        catch {
            await sslStream.SafeDisposeAsync();
            throw;
        }
    }

    protected IClientStream? GetFreeClientStream()
    {
        // Kill all expired client streams
        var now = FastDateTime.Now;
        while (_freeClientStreams.TryDequeue(out var queueItem)) {
            if (queueItem.EnqueueTime + TcpReuseTimeout > now && queueItem.ClientStream.Connected)
                return queueItem.ClientStream;

            queueItem.ClientStream.DisposeWithoutReuse();
        }

        return null;
    }

    private void ClientStreamReuseCallback(IClientStream clientStream)
    {
        // Check if the connector service is disposed
        if (_isDisposed != 0 || !AllowTcpReuse) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "Disposing the reused client stream because the connector service is either disposed or reuse is no longer allowed. " +
                "ClientStreamId: {ClientStreamId}", clientStream.ClientStreamId);
            clientStream.DisposeWithoutReuse();
            return;
        }

        _freeClientStreams.Enqueue(new ClientStreamItem { ClientStream = clientStream });
    }

    public Task RunJob()
    {
        return Task.CompletedTask;
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        // Kill all expired client streams
        var now = FastDateTime.Now;
        while (_freeClientStreams.TryPeek(out var queueItem) && queueItem.EnqueueTime + TcpReuseTimeout <= now) {
            if (_freeClientStreams.TryDequeue(out queueItem))
                queueItem.ClientStream.DisposeWithoutReuse();
        }

        // remove unconnected client streams from shared collection
        // it is just for preventing reuse of unconnected streams.
        // The owner of the stream is responsible for disposing it
        lock (_sharedClientStream)
            _sharedClientStream.RemoveWhere(x => x.Connected);

        return default;
    }

    private void PreventReuseSharedClients()
    {
        lock (_sharedClientStream) {
            foreach (var clientStream in _sharedClientStream)
                clientStream.PreventReuse();

            // release all free client streams
            while (_freeClientStreams.TryDequeue(out var queueItem))
                queueItem.ClientStream.DisposeWithoutReuse();
            _freeClientStreams.Clear();
        }
    }

    public bool AllowTcpReuse {
        get;
        set {
            if (!value)
                PreventReuseSharedClients();
            field = value;
        }
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate == null)
            return false;

        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        var ret = VpnEndPoint.CertificateHash?.SequenceEqual(certificate.GetCertHash()) == true;
        return ret;
    }


    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0) {
            if (disposing) {
                // Stop the cleanup job
                _cleanupJob.Dispose();

                // Prevent reuse of client streams
                PreventReuseSharedClients();

                // Kill and remove all owned client streams
                while (_freeClientStreams.TryDequeue(out var queueItem))
                    queueItem.ClientStream.DisposeWithoutReuse();
                _freeClientStreams.Clear();

                // remove all shared client streams
                lock (_sharedClientStream)
                    _sharedClientStream.Clear();
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private class ClientStreamItem
    {
        public required IClientStream ClientStream { get; init; }
        public DateTime EnqueueTime { get; } = FastDateTime.Now;
    }

    internal class ClientConnectorStatusImpl(ConnectorServiceBase connectorServiceBase)
        : ClientConnectorStatus
    {
        // Note: Count is an approximate snapshot; not synchronized with actual queue operations.
        public override int FreeConnectionCount => connectorServiceBase._freeClientStreams.Count;
    }
}