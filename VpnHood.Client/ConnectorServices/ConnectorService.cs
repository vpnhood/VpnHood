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
using VpnHood.Tunneling.Channels;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling.Messaging;
using VpnHood.Common.Collections;

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorService : IAsyncDisposable, IJob
{
    private readonly ISocketFactory _socketFactory;
    private readonly ConcurrentQueue<ClientStreamItem> _freeClientStreams = new();
    private readonly TaskCollection _disposingTasks = new();
    public TimeSpan TcpTimeout { get; set; }
    public ConnectorEndPointInfo? EndPointInfo { get; set; }
    public JobSection JobSection { get; }
    public ConnectorStat Stat { get; } = new();
    public bool UseHttp { get; set; }

    public ConnectorService(ISocketFactory socketFactory, TimeSpan tcpTimeout)
    {
        _socketFactory = socketFactory;
        TcpTimeout = tcpTimeout;
        JobSection = new JobSection(tcpTimeout);
        JobRunner.Default.Add(this);
    }

    private async Task<IClientStream> GetTlsConnectionToServer(string clientStreamId, CancellationToken cancellationToken)
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

            // start TLS
            var stream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);

            // Establish a TLS connection
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"TLS Authenticating. HostName: {VhLogger.FormatDns(hostName)}...");
            var sslProtocol = Environment.OSVersion.Platform == PlatformID.Win32NT &&
                              Environment.OSVersion.Version.Major < 10
                ? SslProtocols.Tls12 // windows 7
                : SslProtocols.None; //auto

            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = hostName,
                EnabledSslProtocols = sslProtocol
            }, cancellationToken);

            Stat.CreatedConnectionCount++;
            var clientStream = UseHttp
                ? new TcpClientStream(tcpClient, new HttpStream(stream, clientStreamId, hostName), clientStreamId, ReuseStreamClient)
                : new TcpClientStream(tcpClient, stream, clientStreamId);

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
        return await SendRequest(mem.ToArray(), request.RequestId, cancellationToken);
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
        var tcpClientStream = await GetTlsConnectionToServer(requestId, cancellationToken);

        // send request
        await tcpClientStream.Stream.WriteAsync(request, cancellationToken);

        // resend request if the connection is not new
        return tcpClientStream;
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