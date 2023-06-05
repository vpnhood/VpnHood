using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Client.Device;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Client.Exceptions;
using VpnHood.Tunneling.Messaging;
using System.IO;
using VpnHood.Tunneling.ClientStreams;
using System.Collections.Concurrent;
using VpnHood.Common.JobController;
using System.Collections.Generic;

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorService : IAsyncDisposable, IJob
{
    private readonly IPacketCapture _packetCapture;
    private readonly SocketFactory _socketFactory;
    private readonly ConcurrentQueue<ClientStreamItem> _clientStreams = new();

    public TimeSpan TcpTimeout { get; set; }
    public ConnectorEndPointInfo? EndPointInfo { get; set; }
    public JobSection JobSection { get; }
    public ConnectorStat Stat { get; } = new();

    public ConnectorService(IPacketCapture packetCapture, SocketFactory socketFactory, TimeSpan tcpTimeout)
    {
        TcpTimeout = tcpTimeout;
        _packetCapture = packetCapture;
        _socketFactory = socketFactory;
        JobSection = new JobSection(tcpTimeout);
        JobRunner.Default.Add(this);
    }

    private async Task<IClientStream> GetTlsConnectionToServer(CancellationToken cancellationToken)
    {
        if (EndPointInfo == null) throw new InvalidOperationException($"{nameof(EndPointInfo)} is not initialized.");
        var tcpEndPoint = EndPointInfo.TcpEndPoint;
        var hostName = EndPointInfo.HostName;

        // check pool
        var clientStream = GetFreeClientStream();
        if (clientStream != null)
        {
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "A shared ClientStream has been used.");
            return clientStream;
        }

        // create new stream
        var tcpClient = _socketFactory.CreateTcpClient(tcpEndPoint.AddressFamily);
        _socketFactory.SetKeepAlive(tcpClient.Client, true);

        try
        {
            // create tcpConnection
            if (_packetCapture.CanProtectSocket)
                _packetCapture.ProtectSocket(tcpClient.Client);

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
            return new TcpClientStream(tcpClient, stream, null);
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
        while (_clientStreams.TryDequeue(out var queueItem))
        {
            if (queueItem.ClientStream.CheckIsAlive())
            {
                Stat.ReusedConnectionCount++;
                return queueItem.ClientStream;
            }

            _ = queueItem.ClientStream.DisposeAsync(false);
        }

        return null;
    }

    private Task ReuseStreamClient(IClientStream clientStream)
    {
        _clientStreams.Enqueue(new ClientStreamItem { ClientStream = clientStream });
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

    public async Task<IClientStream> SendRequest(RequestCode requestCode, RequestBase requestBase, CancellationToken cancellationToken)
    {
        await using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte((byte)requestCode);
        await StreamUtil.WriteJsonAsync(mem, requestBase, cancellationToken);
        return await SendRequest(mem.ToArray(), cancellationToken);
    }

    public async Task<IClientStream> SendRequest(byte[] request, CancellationToken cancellationToken)
    {
        // get or create free connection
        var tcpClientStream = await GetTlsConnectionToServer(cancellationToken);

        // send request
        await tcpClientStream.Stream.WriteAsync(request, cancellationToken);

        // resend request if the connection is not new
        return tcpClientStream;
    }

    public async ValueTask DisposeAsync()
    {
        var tasks = new List<Task>();
        while (_clientStreams.TryDequeue(out var queueItem))
            tasks.Add(queueItem.ClientStream.DisposeAsync(false).AsTask());

        await Task.WhenAll(tasks);
    }

    public async Task RunJob()
    {
        var now = FastDateTime.Now;

        while (_clientStreams.TryPeek(out var queueItem) && queueItem.EnqueueTime < now - TcpTimeout)
        {
            _clientStreams.TryDequeue(out queueItem);
            await queueItem.ClientStream.DisposeAsync(false);
        }
    }

    private class ClientStreamItem
    {
        public required IClientStream ClientStream { get; init; }
        public DateTime EnqueueTime { get; } = FastDateTime.Now;
    }
}