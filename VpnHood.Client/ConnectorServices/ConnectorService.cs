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

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorService
{
    private readonly IPacketCapture _packetCapture;
    private readonly SocketFactory _socketFactory;
    public TimeSpan TcpTimeout { get; set; }
    public ConnectorEndPointInfo? EndPointInfo { get; set; }

    public ConnectorService(IPacketCapture packetCapture, SocketFactory socketFactory, TimeSpan tcpTimeout)
    {
        TcpTimeout = tcpTimeout;
        _packetCapture = packetCapture;
        _socketFactory = socketFactory;
    }

    private async Task<TcpClientStream> GetTlsConnectionToServer(CancellationToken cancellationToken)
    {
        if (EndPointInfo == null) throw new InvalidOperationException($"{nameof(EndPointInfo)} is not initialized.");
        var tcpEndPoint = EndPointInfo.TcpEndPoint;
        var hostName = EndPointInfo.HostName;

        var tcpClient = _socketFactory.CreateTcpClient(tcpEndPoint.AddressFamily);

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

            return new TcpClientStream(tcpClient, stream);
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

    public async Task<TcpClientStream> SendRequest(RequestCode requestCode, RequestBase requestBase, CancellationToken cancellationToken)
    {
        await using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte((byte)requestCode);
        await StreamUtil.WriteJsonAsync(mem, requestBase, cancellationToken);
        return await SendRequest(mem.ToArray(), cancellationToken);
    }

    public async Task<TcpClientStream> SendRequest(byte[] request, CancellationToken cancellationToken) 
    {
        // get or create free connection
        var tcpClientStream = await GetTlsConnectionToServer(cancellationToken);

        // send request
        await tcpClientStream.Stream.WriteAsync(request, cancellationToken);

        // resend request if the connection is not new
        return tcpClientStream;
    }
}