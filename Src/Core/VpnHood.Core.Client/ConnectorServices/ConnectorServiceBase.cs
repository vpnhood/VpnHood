using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Collections;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Factory;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorServiceBase : IAsyncDisposable, IJob
{
    private readonly ISocketFactory _socketFactory;
    private readonly bool _allowTcpReuse;
    private readonly ConcurrentQueue<ClientStreamItem> _freeClientStreams = new();
    protected readonly TaskCollection DisposingTasks = new();
    private string _apiKey = "";

    public TimeSpan TcpConnectTimeout { get; set; }
    public ConnectorEndPointInfo EndPointInfo { get; }
    public JobSection JobSection { get; }
    public ClientConnectorStat Stat { get; }
    public TimeSpan RequestTimeout { get; private set; }
    public TimeSpan TcpReuseTimeout { get; private set; }
    public int ProtocolVersion { get; private set; } = 5; // 5 is initial connection version

    public ConnectorServiceBase(ConnectorEndPointInfo endPointInfo, ISocketFactory socketFactory,
        TimeSpan tcpConnectTimeout, bool allowTcpReuse)
    {
        _socketFactory = socketFactory;
        _allowTcpReuse = allowTcpReuse;
        Stat = new ClientConnectorStatImpl(this);
        TcpConnectTimeout = tcpConnectTimeout;
        JobSection = new JobSection(tcpConnectTimeout);
        RequestTimeout = TimeSpan.FromSeconds(30);
        TcpReuseTimeout = TimeSpan.FromSeconds(60);
        EndPointInfo = endPointInfo;
        JobRunner.Default.Add(this);
    }

    public void Init(int protocolVersion, TimeSpan tcpRequestTimeout, byte[]? serverSecret,
        TimeSpan tcpReuseTimeout)
    {
        ProtocolVersion = protocolVersion;
        RequestTimeout = tcpRequestTimeout;
        TcpReuseTimeout = tcpReuseTimeout;
        _apiKey = serverSecret != null ? HttpUtil.GetApiKey(serverSecret, TunnelDefaults.HttpPassCheck) : string.Empty;
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream, 
        string streamId, CancellationToken cancellationToken)
    {
        const bool useBuffer = true;
        var binaryStreamType = ProtocolVersion switch {
            <= 5 => string.IsNullOrEmpty(_apiKey) || !_allowTcpReuse ? BinaryStreamType.None : BinaryStreamType.Standard,
            >= 6 => _allowTcpReuse ? BinaryStreamType.Standard : BinaryStreamType.None,
        };

        // write HTTP request
        var header =
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Authorization: ApiKey {_apiKey}\r\n" +
            $"X-BinaryStream: {binaryStreamType}\r\n" +
            $"X-Buffered: {useBuffer}\r\n" +
            $"X-ProtocolVersion: {ProtocolVersion}\r\n" +
            "\r\n";

        // Send header and wait for its response
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken).VhConfigureAwait();
        if (ProtocolVersion <= 5)
            await HttpUtil.ReadHeadersAsync(sslStream, cancellationToken).VhConfigureAwait();

        var srcStream = ProtocolVersion >= 6 ? sslStream : tcpClient.GetStream();
        var clientStream = binaryStreamType == BinaryStreamType.None
            ? new TcpClientStream(tcpClient, sslStream, streamId)
            : new TcpClientStream(tcpClient, new BinaryStreamStandard(srcStream, streamId, useBuffer), streamId, ReuseStreamClient);

        clientStream.RequireHttpResponse = ProtocolVersion >= 6;
        return clientStream;
    }

    protected async Task<IClientStream> GetTlsConnectionToServer(string streamId, CancellationToken cancellationToken)
    {
        if (EndPointInfo == null)   
            throw new InvalidOperationException($"{nameof(EndPointInfo)} has not been initialized.");
        var tcpEndPoint = EndPointInfo.TcpEndPoint;
        var hostName = EndPointInfo.HostName;

        // create new stream
        var tcpClient = _socketFactory.CreateTcpClient(tcpEndPoint.AddressFamily);

        // Client.SessionTimeout does not affect in ConnectAsync
        VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "Connecting to Server... EndPoint: {EndPoint}", VhLogger.Format(tcpEndPoint));
        await tcpClient.VhConnectAsync(tcpEndPoint, TcpConnectTimeout, cancellationToken).VhConfigureAwait();

        // Establish a TLS connection
        var sslStream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);
        VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "TLS Authenticating... HostName: {HostName}",
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

    protected IClientStream? GetFreeClientStream()
    {
        var now = FastDateTime.Now;
        while (_freeClientStreams.TryDequeue(out var queueItem)) {
            // make sure the clientStream timeout has not been reached
            if (queueItem.EnqueueTime + TcpReuseTimeout > now && queueItem.ClientStream.CheckIsAlive())
                return queueItem.ClientStream;

            DisposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));
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
        while (_freeClientStreams.TryPeek(out var queueItem) && queueItem.EnqueueTime + TcpReuseTimeout <= now) {
            _freeClientStreams.TryDequeue(out queueItem);
            DisposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));
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

    public ValueTask DisposeAsync()
    {
        while (_freeClientStreams.TryDequeue(out var queueItem))
            DisposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));

        return DisposingTasks.DisposeAsync();
    }

    private class ClientStreamItem
    {
        public required IClientStream ClientStream { get; init; }
        public DateTime EnqueueTime { get; } = FastDateTime.Now;
    }

    internal class ClientConnectorStatImpl(ConnectorServiceBase connectorServiceBase)
        : ClientConnectorStat
    {
        public override int FreeConnectionCount => connectorServiceBase._freeClientStreams.Count;
    }
}