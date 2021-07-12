using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messages;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Common;

namespace VpnHood.Server
{
    class TcpHost : IDisposable
    {
        private readonly TcpListener _tcpListener;
        private readonly SessionManager _sessionManager;
        private readonly TcpClientFactory _tcpClientFactory;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly SslCertificateManager _sslCertificateManager;
        private const int RemoteHostTimeout = 60000;

        public int OrgStreamReadBufferSize { get; set; }
        public int TunnelStreamReadBufferSize { get; set; }

        public TcpHost(IPEndPoint endPoint, SessionManager sessionManager, SslCertificateManager sslCertificateManager, TcpClientFactory tcpClientFactory)
        {
            _tcpListener = endPoint != null ? new TcpListener(endPoint) : throw new ArgumentNullException(nameof(endPoint));
            _sslCertificateManager = sslCertificateManager ?? throw new ArgumentNullException(nameof(sslCertificateManager));
            _sessionManager = sessionManager;
            _tcpClientFactory = tcpClientFactory;
        }

        public IPEndPoint LocalEndPoint => (IPEndPoint)_tcpListener.LocalEndpoint;

        public void Start()
        {
            var maxRetry = 5;
            for (var i = 0; ; i++)
            {
                try
                {
                    VhLogger.Instance.LogInformation($"Start {VhLogger.FormatTypeName(this)} listener on {VhLogger.Format(_tcpListener.LocalEndpoint)}...\n " +
                        $"{nameof(OrgStreamReadBufferSize)}: {OrgStreamReadBufferSize}, {nameof(TunnelStreamReadBufferSize)}: {TunnelStreamReadBufferSize}");
                    _tcpListener.Start();
                    break;
                }
                catch (SocketException ex) when (i < maxRetry)
                {
                    Console.WriteLine(ex.GetType());
                    VhLogger.Instance.LogError(ex.Message);
                    VhLogger.Instance.LogWarning($"retry: {i + 1} From {maxRetry}");
                    Thread.Sleep(5000);
                }
            }

            var _ = ListenThread();
        }

        private async Task ListenThread()
        {

            try
            {
                // Listening for new connection
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    tcpClient.NoDelay = true;
                    var _ = Task.Run(() => ProcessClient(tcpClient));
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    VhLogger.Instance.LogError($"{ex.Message}");
            }
            finally
            {
                VhLogger.Instance.LogInformation($"{VhLogger.FormatTypeName(this)} Listener has been closed.");
            }

        }

        private async Task ProcessClient(TcpClient tcpClient)
        {
            using var _ = VhLogger.Instance.BeginScope($"RemoteEp: {tcpClient.Client.RemoteEndPoint}");
            var cancellationToken = _cancellationTokenSource.Token;
            TcpClientStream tcpClientStream = null;

            try
            {
                // find certificate by ip 
                var certificate = await _sslCertificateManager.GetCertificate((IPEndPoint)tcpClient.Client.LocalEndPoint);

                // establish SSL
                VhLogger.Instance.LogInformation(GeneralEventId.Tcp, $"TLS Authenticating. CertSubject: {certificate.Subject}...");
                var sslStream = new SslStream(tcpClient.GetStream(), true);
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ClientCertificateRequired = false,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    },
                    cancellationToken);

                tcpClientStream = new TcpClientStream(tcpClient, sslStream);
                await ProcessRequest(tcpClientStream);
            }
            catch (SessionException ex)
            {
                // reply error if it is SessionException
                // do not catch other exception and should not reply anything when sesson has not been created
                await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, new BaseResponse()
                {
                    ResponseCode = ex.ResponseCode,
                    AccessUsage = ex.AccessUsage,
                    SuppressedBy = ex.SuppressedBy,
                    ErrorMessage = ex.Message
                }, cancellationToken);

                tcpClient.Dispose();
                VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"Connection has been closed. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                tcpClientStream?.Dispose();
                tcpClient.Dispose();

                // logging
                if (ex is ObjectDisposedException)
                    VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"Connection has been closed.");
                else
                    VhLogger.Instance.LogError($"{ex}");
            }
        }

        private async Task ProcessRequest(TcpClientStream tcpClientStream)
        {
            // read version
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"Waiting for request...");
            var version = tcpClientStream.Stream.ReadByte();
            if (version == -1)
                throw new Exception("Connection closed without any request!");

            // read request code
            var requestCode = tcpClientStream.Stream.ReadByte();
            switch ((RequestCode)requestCode)
            {
                case RequestCode.Hello:
                    await ProcessHello(tcpClientStream);
                    break;

                case RequestCode.TcpDatagramChannel:
                    await ProcessTcpDatagramChannel(tcpClientStream);
                    break;

                case RequestCode.TcpProxyChannel:
                    await ProcessTcpProxyChannel(tcpClientStream);
                    break;

                case RequestCode.UdpChannel:
                    await ProcessUdpChannel(tcpClientStream);
                    break;

                default:
                    throw new NotSupportedException("Unknown requestCode!");
            }
        }

        private async Task ProcessHello(TcpClientStream tcpClientStream)
        {
            var clientEp = (IPEndPoint)tcpClientStream.TcpClient.Client.RemoteEndPoint;

            VhLogger.Instance.LogInformation(GeneralEventId.Hello, $"Processing hello request... ClientEp: {VhLogger.Format(clientEp)}");
            var cancellationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<HelloRequest>(tcpClientStream.Stream, cancellationToken);

            // Check client version
            if (Version.Parse(request.ClientVersion) < Version.Parse("1.0.0"))
                throw new SessionException(null, ResponseCode.UnsupportedClient, SuppressType.None, "Your client is not supported! Please update your client.");

            // creating a session
            VhLogger.Instance.LogInformation(GeneralEventId.Hello, $"Creating Session... TokenId: {VhLogger.FormatId(request.TokenId)}, ClientId: {VhLogger.FormatId(request.ClientId)}, ClientVersion: {request.ClientVersion}");
            var session = await _sessionManager.CreateSession(request, clientEp.Address);
            session.UseUdpChannel = request.UseUdpChannel;

            // reply hello session
            VhLogger.Instance.LogTrace(GeneralEventId.Hello, $"Replying Hello response. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
            var helloResponse = new HelloResponse()
            {
                SessionId = session.SessionId,
                SessionKey = session.SessionKey,
                UdpKey = session.UdpChannel?.Key,
                UdpPort = session.UdpChannel?.LocalPort ?? 0,
                ServerId = _sessionManager.ServerId,
                ServerVersion = _sessionManager.ServerVersion,
                ServerProtocolVersion = 1,
                SuppressedTo = session.SuppressedTo,
                AccessUsage = session.AccessController.AccessUsage,
                ResponseCode = ResponseCode.Ok
            };
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, helloResponse, cancellationToken);

            // reuse udp channel
            if (!request.UseUdpChannel)
            {
                VhLogger.Instance.LogTrace(GeneralEventId.Hello, $"Reusing Hello stream as a {VhLogger.FormatTypeName<TcpDatagramChannel>()}...");
                await ProcessRequest(tcpClientStream);  //todo remove reuse session support from 1.1.243 and upper
                return;
            }

            tcpClientStream.Dispose();
        }

        private async Task ProcessUdpChannel(TcpClientStream tcpClientStream)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Udp, $"Reading the {VhLogger.FormatTypeName<TcpDatagramChannel>()} request...");
            var cancelationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<UdpChannelRequest>(tcpClientStream.Stream, cancelationToken);

            // finding session
            var session = _sessionManager.GetSession(request);
            session.UseUdpChannel = true;

            // send OK reply
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream,
                new UdpChannelResponse()
                {
                    ResponseCode = ResponseCode.Ok,
                    AccessUsage = session.AccessController.AccessUsage,
                    UdpKey = session.UdpChannel.Key,
                    UdpPort = session.UdpChannel.LocalPort
                }, cancelationToken);

            tcpClientStream.Dispose();
        }

        private async Task ProcessTcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.StreamChannel, $"Reading the {VhLogger.FormatTypeName<TcpDatagramChannel>()} request...");
            var cancelationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<TcpDatagramChannelRequest>(tcpClientStream.Stream, cancelationToken);

            // finding session
            using var _ = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
            var session = _sessionManager.GetSession(request);

            // send OK reply
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream,
                new BaseResponse()
                {
                    ResponseCode = ResponseCode.Ok,
                    AccessUsage = session.AccessController.AccessUsage,
                }, cancelationToken);

            // add channel
            VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel, $"Creating a channel. ClientId: { VhLogger.FormatId(session.ClientId)}");
            var channel = new TcpDatagramChannel(tcpClientStream);

            // disable udpChannel
            session.UseUdpChannel = false;
            session.Tunnel.AddChannel(channel);
        }

        private async Task ProcessTcpProxyChannel(TcpClientStream tcpClientStream)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.StreamChannel, $"Reading the {VhLogger.FormatTypeName<TcpProxyChannel>()} request...");
            var cancelationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<TcpProxyChannelRequest>(tcpClientStream.Stream, cancelationToken);

            // find session
            using var _ = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatId(request.SessionId)}");
            var isRequestedEpException = false;
            try
            {
                var session = _sessionManager.GetSession(request);

                // connect to requested site
                VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel, $"Connecting to the requested endpoint. RequestedEP: {VhLogger.FormatDns(request.DestinationAddress)}:{request.DestinationPort}");
                var requestedEndPoint = new IPEndPoint(IPAddress.Parse(request.DestinationAddress), request.DestinationPort);
                
                isRequestedEpException = true;
                var tcpClient2 = _tcpClientFactory.Create();
                //var tcpClient2 = _tcpClientFactory.CreateAndConnect(requestedEndPoint);
                tcpClient2.NoDelay = true;
                tcpClient2.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                await Util.TcpClient_ConnectAsync(tcpClient2, requestedEndPoint.Address, requestedEndPoint.Port, RemoteHostTimeout, cancelationToken);
                isRequestedEpException = false;

                // send response
                var response = new BaseResponse()
                {
                    AccessUsage = session.AccessController.AccessUsage,
                    ResponseCode = ResponseCode.Ok,
                };
                await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, response, cancelationToken);

                // Dispose ssl stream and replace it with a HeadCryptor
                tcpClientStream.Stream.Dispose();
                tcpClientStream.Stream = StreamHeadCryptor.Create(tcpClientStream.TcpClient.GetStream(),
                    request.CipherKey, null, request.CipherLength);

                // add the connection
                VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel, $"Adding the connection. ClientId: { VhLogger.FormatId(session.ClientId)}, CipherLength: {request.CipherLength}");
                var channel = new TcpProxyChannel(new TcpClientStream(tcpClient2, tcpClient2.GetStream()), tcpClientStream,
                    orgStreamReadBufferSize: OrgStreamReadBufferSize, tunnelStreamReadBufferSize: TunnelStreamReadBufferSize);
                session.Tunnel.AddChannel(channel);
            }
            catch (Exception ex) when (isRequestedEpException)
            {
                throw new SessionException(null, ResponseCode.GeneralError, SuppressType.None, ex.Message);
            }
        }
        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _tcpListener.Stop();
        }
    }
}