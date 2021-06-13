using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messages;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Server
{
    class TcpHost : IDisposable
    {
        private readonly TcpListener _tcpListener;
        private readonly SessionManager _sessionManager;
        private readonly TcpClientFactory _tcpClientFactory;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly SslCertificateManager _sslCertificateManager;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Instance;


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
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<TcpHost>()}");
            var tasks = new ConcurrentDictionary<Task, int>();

            var maxRetry = 5;
            for (var i = 0; ; i++)
            {
                try
                {
                    _logger.LogInformation($"Start listening on {VhLogger.Format(_tcpListener.LocalEndpoint)}...");
                    _tcpListener.Start();
                    break;
                }
                catch (SocketException ex) when (i < maxRetry)
                {
                    Console.WriteLine(ex.GetType());
                    _logger.LogError(ex.Message);
                    _logger.LogWarning($"retry: {i + 1} From {maxRetry}");
                    Thread.Sleep(5000);
                }
            }

            var task = ListenThread();
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
                    //tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    var _ = Task.Run(() => ProcessClient(tcpClient));
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    _logger.LogError($"{ex.Message}");
            }
            finally
            {
                _logger.LogInformation($"{VhLogger.FormatTypeName(this)} Listener has been closed.");
            }

        }

        private async Task ProcessClient(TcpClient tcpClient)
        {
            using var _ = _logger.BeginScope($"RemoteEp: {tcpClient.Client.RemoteEndPoint}");
            var cancellationToken = _cancellationTokenSource.Token;
            TcpClientStream tcpClientStream = null;

            try
            {
                // find certificate by ip 
                var certificate = await _sslCertificateManager.GetCertificate((IPEndPoint)tcpClient.Client.LocalEndPoint);

                // establish SSL
                _logger.LogInformation(GeneralEventId.Tcp, $"TLS Authenticating. CertSubject: {certificate.Subject}...");
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
                _logger.LogTrace(GeneralEventId.Tcp, $"Connection has been closed. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                tcpClientStream?.Dispose();
                tcpClient.Dispose();

                // logging
                if (ex is ObjectDisposedException)
                    _logger.LogTrace(GeneralEventId.Tcp, $"Connection has been closed.");
                else
                    _logger.LogError($"{ex}");
            }
        }

        private async Task ProcessRequest(TcpClientStream tcpClientStream)
        {
            // read version
            _logger.LogTrace(GeneralEventId.Tcp, $"Waiting for request...");
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

            _logger.LogInformation(GeneralEventId.Hello, $"Processing hello request... ClientEp: {VhLogger.Format(clientEp)}");
            var cancellationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<HelloRequest>(tcpClientStream.Stream, cancellationToken);

            // creating a session
            _logger.LogInformation(GeneralEventId.Hello, $"Creating Session... TokenId: {VhLogger.FormatId(request.TokenId)}, ClientId: {VhLogger.FormatId(request.ClientId)}, ClientVersion: {request.ClientVersion}");
            var session = await _sessionManager.CreateSession(request, clientEp.Address);
            session.UseUdpChannel = request.UseUdpChannel;

            // reply hello session
            _logger.LogTrace(GeneralEventId.Hello, $"Replying Hello response. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
            var helloResponse = new HelloResponse()
            {
                SessionId = session.SessionId,
                SessionKey = session.SessionKey,
                UdpKey = session.UdpChannel?.Key,
                UdpPort = session.UdpChannel?.LocalPort ?? 0,
                ServerId = _sessionManager.ServerId,
                SuppressedTo = session.SuppressedTo,
                AccessUsage = session.AccessController.AccessUsage,
                ResponseCode = ResponseCode.Ok
            };
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, helloResponse, cancellationToken);

            // reuse udp channel
            if (!request.UseUdpChannel)
            {
                _logger.LogTrace(GeneralEventId.Hello, $"Reusing Hello stream as a {VhLogger.FormatTypeName<TcpDatagramChannel>()}...");
                //await ProcessRequest(tcpClientStream);  //todo remove reuse session support from 1.1.243 and upper
                return;
            }

            tcpClientStream.Dispose();
        }

        private async Task ProcessUdpChannel(TcpClientStream tcpClientStream)
        {
            _logger.LogInformation(GeneralEventId.Udp, $"Reading the {VhLogger.FormatTypeName<TcpDatagramChannel>()} request...");
            var cancelationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<UdpChannelRequest>(tcpClientStream.Stream, cancelationToken);

            // finding session
            var session = _sessionManager.GetSession(request);
            session.UseUdpChannel = true;

            // send OK reply
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream,
                new UdpChannelResponse() { 
                    ResponseCode = ResponseCode.Ok, 
                    AccessUsage = session.AccessController.AccessUsage,
                    UdpKey = session.UdpChannel.Key,
                    UdpPort = session.UdpChannel.LocalPort
                }, cancelationToken);

            tcpClientStream.Dispose();
        }

        private async Task ProcessTcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            _logger.LogInformation(GeneralEventId.StreamChannel, $"Reading the {VhLogger.FormatTypeName<TcpDatagramChannel>()} request...");
            var cancelationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<TcpDatagramChannelRequest>(tcpClientStream.Stream, cancelationToken);

            // finding session
            using var _ = _logger.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
            var session = _sessionManager.GetSession(request);

            // send OK reply
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream,
                new BaseResponse()
                {
                    ResponseCode = ResponseCode.Ok,
                    AccessUsage = session.AccessController.AccessUsage,
                }, cancelationToken);

            // add channel
            _logger.LogTrace(GeneralEventId.DatagramChannel, $"Creating a channel. ClientId: { VhLogger.FormatId(session.ClientId)}");
            var channel = new TcpDatagramChannel(tcpClientStream);

            // disable udpChannel
            session.UseUdpChannel = false;
            session.Tunnel.AddChannel(channel);
        }

        private async Task ProcessTcpProxyChannel(TcpClientStream tcpClientStream)
        {
            _logger.LogInformation(GeneralEventId.StreamChannel, $"Reading the {VhLogger.FormatTypeName<TcpProxyChannel>()} request...");
            var cancelationToken = _cancellationTokenSource.Token;
            var request = await StreamUtil.ReadJsonAsync<TcpProxyChannelRequest>(tcpClientStream.Stream, cancelationToken);

            // find session
            using var _ = _logger.BeginScope($"SessionId: {VhLogger.FormatId(request.SessionId)}");
            var isRequestedEpException = false;
            try
            {
                var session = _sessionManager.GetSession(request);

                // connect to requested site
                _logger.LogTrace(GeneralEventId.StreamChannel, $"Connecting to the requested endpoint. RequestedEP: {VhLogger.FormatDns(request.DestinationAddress)}:{request.DestinationPort}");
                var requestedEndPoint = new IPEndPoint(IPAddress.Parse(request.DestinationAddress), request.DestinationPort);
                isRequestedEpException = true;
                var tcpClient2 = _tcpClientFactory.CreateAndConnect(requestedEndPoint);
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
                _logger.LogTrace(GeneralEventId.StreamChannel, $"Adding the connection. ClientId: { VhLogger.FormatId(session.ClientId)}, CipherLength: {request.CipherLength}");
                var channel = new TcpProxyChannel(new TcpClientStream(tcpClient2, tcpClient2.GetStream()), tcpClientStream);
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