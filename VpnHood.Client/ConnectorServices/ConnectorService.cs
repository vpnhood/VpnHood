using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Client.Exceptions;
using VpnHood.Tunneling.ClientStreams;
using VpnHood.Common.JobController;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling.Messaging;
using VpnHood.Common.Collections;
using VpnHood.Tunneling.Channels.Streams;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorService : IAsyncDisposable, IJob
{
    private readonly ISocketFactory _socketFactory;
    private readonly ConcurrentQueue<ClientStreamItem> _freeClientStreams = new();
    private readonly TaskCollection _disposingTasks = new();
    private string _apiKey = "";

    public TimeSpan TcpTimeout { get; set; }
    public ConnectorEndPointInfo? EndPointInfo { get; set; }
    public JobSection JobSection { get; }
    public ConnectorStat Stat { get; }
    public TimeSpan RequestTimeout { get; private set; }
    public TimeSpan TcpReuseTimeout { get; private set; }
    public int ServerProtocolVersion { get; private set; }

    public ConnectorService(ISocketFactory socketFactory, TimeSpan tcpTimeout)
    {
        _socketFactory = socketFactory;
        Stat = new ConnectorStatImpl(this);
        TcpTimeout = tcpTimeout;
        JobSection = new JobSection(tcpTimeout);
        RequestTimeout = TimeSpan.FromSeconds(30);
        TcpReuseTimeout = TimeSpan.FromSeconds(60);
        JobRunner.Default.Add(this);
    }

    public void Init(int serverProtocolVersion, TimeSpan tcpRequestTimeout, TimeSpan tcpReuseTimeout, byte[]? serverSecret)
    {
        ServerProtocolVersion = serverProtocolVersion;
        RequestTimeout = tcpRequestTimeout;
        TcpReuseTimeout = tcpReuseTimeout;
        _apiKey = serverSecret != null ? HttpUtil.GetApiKey(serverSecret, TunnelDefaults.HttpPassCheck) : string.Empty;
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream, string streamId, CancellationToken cancellationToken)
    {
        var streamSecret = VhUtil.GenerateKey(128); // deprecated by 450 and upper
        var version = ServerProtocolVersion >= 5 ? 3 : 2;
        var useBinaryStream = version >= 3 && !string.IsNullOrEmpty(_apiKey);

        // write HTTP request
        var header =
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Authorization: ApiKey {_apiKey}\r\n" +
            $"X-Version: {version}\r\n" +
            $"X-Secret: {Convert.ToBase64String(streamSecret)}\r\n" +
            $"X-BinaryStream: {useBinaryStream}\r\n" +
            "\r\n";

        // Send header and wait for its response
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken); // secret
        await HttpUtil.ReadHeadersAsync(sslStream, cancellationToken);

        // deprecated legacy by version >= 450
#pragma warning disable CS0618 // Type or member is obsolete
        if (version == 2)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return new TcpClientStream(tcpClient, sslStream, streamId);

            // dispose Ssl
            await sslStream.DisposeAsync();
            return new TcpClientStream(tcpClient, new CryptoBinaryStream(tcpClient.GetStream(), streamId, streamSecret), streamId, ReuseStreamClient);
        }
#pragma warning restore CS0618 // Type or member is obsolete

        return useBinaryStream
            ? new TcpClientStream(tcpClient, new BinaryStream(tcpClient.GetStream(), streamId), streamId, ReuseStreamClient)
            : new TcpClientStream(tcpClient, sslStream, streamId);
    }

    private async Task<IClientStream> GetTlsConnectionToServer(string streamId, CancellationToken cancellationToken)
    {
        if (EndPointInfo == null) throw new InvalidOperationException($"{nameof(EndPointInfo)} has not been initialized.");
        var tcpEndPoint = EndPointInfo.TcpEndPoint;
        var hostName = EndPointInfo.HostName;

        // create new stream
        var tcpClient = _socketFactory.CreateTcpClient(tcpEndPoint.AddressFamily);

        try
        {
            // Client.SessionTimeout does not affect in ConnectAsync
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "Connecting to Server... EndPoint: {EndPoint}", VhLogger.Format(tcpEndPoint));

            await VhUtil.RunTask(tcpClient.ConnectAsync(tcpEndPoint.Address, tcpEndPoint.Port), TcpTimeout, cancellationToken);

            // Establish a TLS connection
            var sslStream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "TLS Authenticating... HostName: {HostName}", VhLogger.FormatHostName(hostName));
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = hostName,
                EnabledSslProtocols = SslProtocols.None // auto
            }, cancellationToken);

            var clientStream = await CreateClientStream(tcpClient, sslStream, streamId, cancellationToken);
            lock (Stat) Stat.CreatedConnectionCount++;
            return clientStream;
        }
        catch (MaintenanceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConnectorEstablishException(ex.Message, ex);
        }
    }

    private IClientStream? GetFreeClientStream()
    {
        var now = FastDateTime.Now;
        while (_freeClientStreams.TryDequeue(out var queueItem))
        {
            // make sure the clientStream timeout has not been reached
            if (queueItem.EnqueueTime + TcpReuseTimeout > now && queueItem.ClientStream.CheckIsAlive())
                return queueItem.ClientStream;

            _disposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));
        }

        return null;
    }

    private Task ReuseStreamClient(IClientStream clientStream)
    {
        _freeClientStreams.Enqueue(new ClientStreamItem { ClientStream = clientStream });
        return Task.CompletedTask;
    }

    public Task RunJob()
    {
        var now = FastDateTime.Now;
        while (_freeClientStreams.TryPeek(out var queueItem) && queueItem.EnqueueTime + TcpReuseTimeout <= now)
        {
            _freeClientStreams.TryDequeue(out queueItem);
            _disposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));
        }

        return Task.CompletedTask;
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate == null!) // for android 6 (API 23)
            return true;

        // check maintenance certificate
        var parts = certificate.Subject.Split(",");
        if (parts.Any(x => x.Trim().Equals("OU=MT", StringComparison.OrdinalIgnoreCase)))
            throw new MaintenanceException();

        var ret = sslPolicyErrors == SslPolicyErrors.None ||
               EndPointInfo?.CertificateHash?.SequenceEqual(certificate.GetCertHash()) == true;
        return ret;
    }

    public async Task<ConnectorRequestResult<T>> SendRequest<T>(EventId eventId, ClientRequest request, CancellationToken cancellationToken)
        where T : SessionResponseBase
    {
        VhLogger.Instance.LogTrace(eventId,
            "Sending a request. RequestCode: {RequestCode}, RequestId: {RequestId}",
            (RequestCode)request.RequestCode, request.RequestId);

        // set request timeout
        using var cancellationTokenSource = new CancellationTokenSource(RequestTimeout);
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
        cancellationToken = linkedCancellationTokenSource.Token;

        await using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte(request.RequestCode);
        await StreamUtil.WriteJsonAsync(mem, request, cancellationToken);
        var ret = await SendRequest<T>(mem.ToArray(), request.RequestId, cancellationToken);

        // log the response
        VhLogger.Instance.LogTrace(eventId, "Received a response... ErrorCode: {ErrorCode}.",
            ret.Response.ErrorCode);

        lock (Stat) Stat.RequestCount++;
        return ret;
    }

    private async Task<ConnectorRequestResult<T>> SendRequest<T>(byte[] request, string requestId, CancellationToken cancellationToken)
        where T : SessionResponseBase
    {
        // try reuse
        var clientStream = GetFreeClientStream();
        if (clientStream != null)
        {
            VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                "A shared ClientStream has been reused. ClientStreamId: {ClientStreamId}, LocalEp: {LocalEp}",
                clientStream.ClientStreamId, clientStream.IpEndPointPair.LocalEndPoint);

            try
            {
                // we may use this buffer to encrypt so clone it for retry
                await clientStream.Stream.WriteAsync((byte[])request.Clone(), cancellationToken);
                var response = await StreamUtil.ReadJsonAsync<T>(clientStream.Stream, cancellationToken);
                lock (Stat) Stat.ReusedConnectionSucceededCount++;
                return new ConnectorRequestResult<T>
                {
                    Response = response,
                    ClientStream = clientStream
                };
            }
            catch (Exception ex)
            {
                // dispose the connection and retry with new connection
                lock (Stat) Stat.ReusedConnectionFailedCount++;
                _disposingTasks.Add(clientStream.DisposeAsync(false));
                VhLogger.LogError(GeneralEventId.TcpLife, ex,
                    "Error in reusing the ClientStream. Try a new connection. ClientStreamId: {ClientStreamId}",
                    clientStream.ClientStreamId);
            }
        }

        // create free connection
        clientStream = await GetTlsConnectionToServer(requestId, cancellationToken);

        // send request
        await clientStream.Stream.WriteAsync(request, cancellationToken);
        var response2 = await StreamUtil.ReadJsonAsync<T>(clientStream.Stream, cancellationToken);
        return new ConnectorRequestResult<T>
        {
            Response = response2,
            ClientStream = clientStream
        };
    }

    public ValueTask DisposeAsync()
    {
        while (_freeClientStreams.TryDequeue(out var queueItem))
            _disposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));

        return _disposingTasks.DisposeAsync();
    }

    private class ClientStreamItem
    {
        public required IClientStream ClientStream { get; init; }
        public DateTime EnqueueTime { get; } = FastDateTime.Now;
    }

    public class ConnectorStatImpl : ConnectorStat
    {
        private readonly ConnectorService _connectorService;
        public override int FreeConnectionCount => _connectorService._freeClientStreams.Count;

        internal ConnectorStatImpl(ConnectorService connectorService)
        {
            _connectorService = connectorService;
        }
    }
}