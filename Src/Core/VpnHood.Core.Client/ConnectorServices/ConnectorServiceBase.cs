using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorServiceBase : IDisposable
{
    private readonly ISocketFactory _socketFactory;
    private readonly ConcurrentQueue<ClientStreamItem> _freeClientStreams = new();
    private readonly HashSet<IClientStream> _sharedClientStream = new(50); // for preventing reuse
    private readonly VhJob _cleanupJob;
    private bool _disposed;
    private bool _allowTcpReuse;

    public TimeSpan TcpConnectTimeout { get; set; }
    public ConnectorEndPointInfo EndPointInfo { get; }
    public JobSection JobSection { get; }
    public ClientConnectorStat Stat { get; }
    public TimeSpan RequestTimeout { get; private set; }
    public TimeSpan TcpReuseTimeout { get; private set; }
    public int ProtocolVersion { get; private set; } = 6;

    public ConnectorServiceBase(ConnectorEndPointInfo endPointInfo, ISocketFactory socketFactory,
        TimeSpan tcpConnectTimeout, bool allowTcpReuse)
    {
        _socketFactory = socketFactory;
        _allowTcpReuse = allowTcpReuse;
        Stat = new ClientConnectorStatImpl(this);
        TcpConnectTimeout = tcpConnectTimeout;
        JobSection = new JobSection(tcpConnectTimeout);
        RequestTimeout = TimeSpan.FromSeconds(30);
        TcpReuseTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30);
        EndPointInfo = endPointInfo;
        _cleanupJob = new VhJob(CleanupTimeoutConnections, TimeSpan.FromSeconds(60), "ConnectorCleanup");
    }

    public void Init(int protocolVersion, TimeSpan tcpRequestTimeout, byte[]? serverSecret,
        TimeSpan tcpReuseTimeout)
    {
        ProtocolVersion = protocolVersion;
        RequestTimeout = tcpRequestTimeout;
        TcpReuseTimeout = tcpReuseTimeout;
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream,
        string streamId, CancellationToken cancellationToken)
    {
        const bool useBuffer = true;
        var streamType = _allowTcpReuse ? TunnelStreamType.Standard : TunnelStreamType.None;

        // write HTTP request
        var header =
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"X-BinaryStream: {streamType}\r\n" +
            $"X-Buffered: {useBuffer}\r\n" +
            $"X-ProtocolVersion: {ProtocolVersion}\r\n" +
            "\r\n";

        // Send header and wait for its response
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken).VhConfigureAwait();
        var clientStream = streamType == TunnelStreamType.None
            ? new TcpClientStream(tcpClient, sslStream, streamId)
            : new TcpClientStream(tcpClient, new BinaryStreamStandard(sslStream, streamId, useBuffer), streamId, ClientStreamReuseCallback);

        // add to sharable client stream for disposal
        if (clientStream.Stream is BinaryStreamStandard)
            lock (_sharedClientStream)
                _sharedClientStream.Add(clientStream);

        clientStream.RequireHttpResponse = true;
        return clientStream;
    }

    protected async Task<IClientStream> GetTlsConnectionToServer(string streamId, CancellationToken cancellationToken)
    {
        var tcpEndPoint = EndPointInfo.TcpEndPoint;

        // create new stream
        var tcpClient = _socketFactory.CreateTcpClient(tcpEndPoint);
        try {
            // Client.SessionTimeout does not affect in ConnectAsync
            VhLogger.Instance.LogDebug(GeneralEventId.Tcp, "Establishing a new TCP to the Server... EndPoint: {EndPoint}",
                VhLogger.Format(tcpEndPoint));
            await tcpClient.VhConnectAsync(tcpEndPoint, TcpConnectTimeout, cancellationToken).VhConfigureAwait();
            return await GetTlsConnectionToServer(streamId, tcpClient, cancellationToken);
        }
        catch {
            tcpClient.Dispose();
            throw;
        }
    }

    private async Task<IClientStream> GetTlsConnectionToServer(string streamId, TcpClient tcpClient, CancellationToken cancellationToken)
    {
        // Establish a TLS connection
        var sslStream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);
        try {
            var hostName = EndPointInfo.HostName;
            VhLogger.Instance.LogDebug(GeneralEventId.Tcp, "TLS Authenticating... HostName: {HostName}",
                VhLogger.FormatHostName(hostName));

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = hostName,
                EnabledSslProtocols = SslProtocols.None // auto
            }, cancellationToken)
                .VhConfigureAwait();

            var clientStream = await CreateClientStream(tcpClient, sslStream, streamId, cancellationToken).VhConfigureAwait();
            lock (Stat) Stat.CreatedConnectionCount++;
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
        if (_disposed || !_allowTcpReuse) {
            VhLogger.Instance.LogDebug(GeneralEventId.Tcp,
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

    private ValueTask CleanupTimeoutConnections(CancellationToken cancellationToken)
    {
        // Kill all expired client streams
        var now = FastDateTime.Now;
        while (_freeClientStreams.TryPeek(out var queueItem) && queueItem.EnqueueTime + TcpReuseTimeout <= now) {
            _freeClientStreams.TryDequeue(out queueItem);
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

    public bool AllowTcpReuse 
    {
        get => _allowTcpReuse;
        set {
            if (value == false)
                PreventReuseSharedClients();
            _allowTcpReuse = value;
        }
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate == null!) // for android 6 (API 23)
            return true;

        var ret = sslPolicyErrors == SslPolicyErrors.None ||
                  EndPointInfo.CertificateHash?.SequenceEqual(certificate.GetCertHash()) == true;

        return ret;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop the cleanup job
        _cleanupJob.Dispose();
        
        // Prevent reuse of client streams
        PreventReuseSharedClients();

        // Kill and remove all owned client streams
        while (_freeClientStreams.TryDequeue(out var queueItem))
            queueItem.ClientStream.DisposeWithoutReuse();
        _freeClientStreams.Clear();

        // remove all shared client streams
        _sharedClientStream.Clear();
    }

    private class ClientStreamItem
    {
        public required IClientStream ClientStream { get; init; }
        public DateTime EnqueueTime { get; } = FastDateTime.Now;
    }

    internal class ClientConnectorStatImpl(ConnectorServiceBase connectorServiceBase)
        : ClientConnectorStat
    {
        // Note: Count is an approximate snapshot; not synchronized with actual queue operations.
        public override int FreeConnectionCount => connectorServiceBase._freeClientStreams.Count;
    }
}