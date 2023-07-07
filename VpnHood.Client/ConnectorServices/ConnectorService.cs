using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Client.Exceptions;
using System.IO;
using VpnHood.Tunneling.ClientStreams;
using System.Collections.Concurrent;
using VpnHood.Common.JobController;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling.Messaging;
using VpnHood.Common.Collections;
using VpnHood.Tunneling.Channels.Streams;
using System.Text;
using VpnHood.Tunneling.Utils;
using System.Net.Sockets;

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
    public ConnectorStat Stat { get; } = new();
    public bool UseBinaryStream { get; set; }

    public byte[]? ServerKey
    {
        set => _apiKey = value != null ? HttpUtil.GetApiKey(value, TunnelDefaults.HttpPassCheck) : "";
    }

    public ConnectorService(ISocketFactory socketFactory, TimeSpan tcpTimeout)
    {
        _socketFactory = socketFactory;
        TcpTimeout = tcpTimeout;
        JobSection = new JobSection(tcpTimeout);
        JobRunner.Default.Add(this);
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream, string streamId, CancellationToken cancellationToken)
    {
        // compatibility
        if (!UseBinaryStream)
            return new TcpClientStream(tcpClient, sslStream, streamId);

        var streamSecret = VhUtil.GenerateKey(128);

        // write HTTP request
        var header =
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Authorization: ApiKey {_apiKey}\r\n" +
            $"X-Version: 2\r\n" +
            $"X-Secret: {Convert.ToBase64String(streamSecret)}\r\n" +
            "\r\n";

        // Wait for result
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken); // secret
        await HttpUtil.ReadHeadersAsync(sslStream, cancellationToken);

        //if (!string.IsNullOrEmpty(_apiKey))
          //  await sslStream.DisposeAsync(); //todo

        return string.IsNullOrEmpty(_apiKey)
            ? new TcpClientStream(tcpClient, sslStream, streamId)
            : new TcpClientStream(tcpClient, new BinaryStream(tcpClient.GetStream(), streamId, streamSecret), streamId, ReuseStreamClient);
    }

    private async Task<IClientStream> GetTlsConnectionToServer(string streamId, CancellationToken cancellationToken)
    {
        if (EndPointInfo == null) throw new InvalidOperationException($"{nameof(EndPointInfo)} is not initialized.");
        var tcpEndPoint = EndPointInfo.TcpEndPoint;
        var hostName = EndPointInfo.HostName;

        // create new stream
        var tcpClient = _socketFactory.CreateTcpClient(tcpEndPoint.AddressFamily);

        try
        {
            // Client.SessionTimeout does not affect in ConnectAsync
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"Connecting to Server: {VhLogger.Format(tcpEndPoint)}...");
            await VhUtil.RunTask(tcpClient.ConnectAsync(tcpEndPoint.Address, tcpEndPoint.Port), TcpTimeout, cancellationToken);

            var sslStream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);

            // Establish a TLS connection
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"TLS Authenticating. HostName: {VhLogger.FormatDns(hostName)}...");
            var sslProtocol = Environment.OSVersion.Platform == PlatformID.Win32NT &&
                              Environment.OSVersion.Version.Major < 10
                ? SslProtocols.Tls12 // windows 7
                : SslProtocols.None; //auto

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = hostName,
                EnabledSslProtocols = sslProtocol
            }, cancellationToken);

            Stat.CreatedConnectionCount++;
            var clientStream = await CreateClientStream(tcpClient, sslStream, streamId, cancellationToken);
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
        while (_freeClientStreams.TryDequeue(out var queueItem))
        {
            Stat.FreeConnectionCount--;
            if (queueItem.ClientStream.CheckIsAlive())
                return queueItem.ClientStream;

            _disposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));
        }

        return null;
    }

    private Task ReuseStreamClient(IClientStream clientStream)
    {
        _freeClientStreams.Enqueue(new ClientStreamItem { ClientStream = clientStream });
        Stat.FreeConnectionCount++;
        return Task.CompletedTask;
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // check maintenance certificate
        var parts = certificate.Subject.Split(",");
        if (parts.Any(x => x.Trim().Equals("OU=MT", StringComparison.OrdinalIgnoreCase)))
            throw new MaintenanceException();

        return sslPolicyErrors == SslPolicyErrors.None ||
               EndPointInfo?.CertificateHash?.SequenceEqual(certificate.GetCertHash()) == true;
    }

    public async Task<IClientStream> SendRequest(ClientRequest request, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Request,
            "Sending a request. RequestCode: {RequestCode}, RequestId: {RequestId}",
            (RequestCode)request.RequestCode, request.RequestId);

        await using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte(request.RequestCode);
        await StreamUtil.WriteJsonAsync(mem, request, cancellationToken);
        var clientStream = await SendRequest(mem.ToArray(), request.RequestId, cancellationToken);
        return clientStream;
    }

    private async Task<IClientStream> SendRequest(byte[] request, string requestId, CancellationToken cancellationToken)
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
                await clientStream.Stream.WriteAsync(request, cancellationToken);
                _ = await clientStream.Stream.ReadAsync(request, 0, 0, cancellationToken); // wait for any available data 
                Stat.ReusedConnectionSucceededCount++;
                clientStream.ClientStreamId = requestId;
                return clientStream;
            }
            catch (Exception ex)
            {
                // dispose the connection and retry with new connection
                Stat.ReusedConnectionFailedCount++;
                _disposingTasks.Add(clientStream.DisposeAsync(false));
                VhLogger.LogError(GeneralEventId.TcpLife, ex,
                    "Error in reusing the ClientStream. Try a new connection. ClientStreamId: {ClientStreamId}",
                    clientStream.ClientStreamId);
            }
        }

        // get or create free connection
        clientStream = await GetTlsConnectionToServer(requestId, cancellationToken);

        // send request
        await clientStream.Stream.WriteAsync(request, cancellationToken);
        return clientStream;
    }

    public async ValueTask DisposeAsync()
    {
        while (_freeClientStreams.TryDequeue(out var queueItem))
            _disposingTasks.Add(queueItem.ClientStream.DisposeAsync(false));

        await _disposingTasks.DisposeAsync();
    }

    public async Task RunJob()
    {
        var now = FastDateTime.Now;

        var tcpReuseTimeout = TunnelDefaults.TcpReuseTimeout - TunnelDefaults.TcpReuseTimeout / 3; // it must be less than server
        while (_freeClientStreams.TryPeek(out var queueItem) && queueItem.EnqueueTime < now - tcpReuseTimeout)
        {
            _freeClientStreams.TryDequeue(out queueItem);
            await queueItem.ClientStream.DisposeAsync(false);
        }
    }

    private class ClientStreamItem
    {
        public required IClientStream ClientStream { get; init; }
        public DateTime EnqueueTime { get; } = FastDateTime.Now;
    }
}