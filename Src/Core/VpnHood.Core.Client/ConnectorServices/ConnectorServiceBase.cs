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

internal class ConnectorServiceBase : IDisposable, IJob
{
    private readonly ISocketFactory _socketFactory;
    private readonly bool _allowTcpReuse;
    private readonly ConcurrentQueue<ClientStreamItem> _freeClientStreams = new();
    private bool _disposed;

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
        TcpReuseTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30); ;
        EndPointInfo = endPointInfo;
        JobRunner.Default.Add(this);
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
        var binaryStreamType = _allowTcpReuse ? BinaryStreamType.Standard : BinaryStreamType.None;

        // write HTTP request
        var header =
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"X-BinaryStream: {binaryStreamType}\r\n" +
            $"X-Buffered: {useBuffer}\r\n" +
            $"X-ProtocolVersion: {ProtocolVersion}\r\n" +
            "\r\n";

        // Send header and wait for its response
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken).VhConfigureAwait();
        var clientStream = binaryStreamType == BinaryStreamType.None
            ? new TcpClientStream(tcpClient, sslStream, streamId)
            : new TcpClientStream(tcpClient, new BinaryStreamStandard(sslStream, streamId, useBuffer), streamId, ReuseClientStream);

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

    private void ReuseClientStream(IClientStream clientStream)
    {
        // Check if the connector service is disposed
        if (_disposed) {
            VhLogger.Instance.LogDebug(GeneralEventId.Tcp, 
                "Disposing the reused client stream because the connector service is disposed. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);
            clientStream.DisposeWithoutReuse();
            return;
        }

        Console.WriteLine($"zzz: {clientStream.ClientStreamId}"); //todo
        _freeClientStreams.Enqueue(new ClientStreamItem { ClientStream = clientStream });
    }

    public Task RunJob()
    {
        // Kill all expired client streams
        var now = FastDateTime.Now;
        while (_freeClientStreams.TryPeek(out var queueItem) && queueItem.EnqueueTime + TcpReuseTimeout <= now) {
            _freeClientStreams.TryDequeue(out queueItem);
            queueItem.ClientStream.DisposeWithoutReuse();
        }

        return Task.CompletedTask;
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

        // Kill all owned client streams
        while (_freeClientStreams.TryDequeue(out var queueItem))
            queueItem.ClientStream.DisposeWithoutReuse();

        _freeClientStreams.Clear();
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