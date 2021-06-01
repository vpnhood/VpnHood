using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messages;
using System.Linq;

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
        private ILogger _logger => VhLogger.Current;


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
                    var _ = ProcessClient(tcpClient);
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

            try
            {
                // find certificate by ip 

                // establish SSL
                var certificate = await _sslCertificateManager.GetCertificate((IPEndPoint)tcpClient.Client.LocalEndPoint);
                _logger.LogInformation(GeneralEventId.Tcp, $"TLS Authenticating. CertSubject: {certificate.Subject}...");
                var sslStream = new SslStream(tcpClient.GetStream(), true);
                await sslStream.AuthenticateAsServerAsync(certificate, false, true);

                var tcpClientStream = new TcpClientStream(tcpClient, sslStream);
                if (!await ProcessRequestTask(tcpClientStream))
                {
                    _logger.LogTrace(GeneralEventId.Tcp, $"Disposing the connection...");
                    tcpClientStream.Dispose();
                }
            }
            catch (Exception ex)
            {
                tcpClient.Dispose();

                // logging
                if (!(ex is ObjectDisposedException))
                    _logger.LogError($"{ex}");
                else
                    _logger.LogTrace(GeneralEventId.Tcp, $"Connection has been closed.");
            }
        }

        private Task<bool> ProcessRequestTask(TcpClientStream tcpClientStream)
        {
            return Task.Run(async () =>
           {
               return await ProcessRequest(tcpClientStream);
           });
        }


        private Task<bool> ProcessRequest(TcpClientStream tcpClientStream, bool afterHello = false)
        {
            // read version
            _logger.LogTrace(GeneralEventId.Tcp, $"Waiting for request...");
            var version = tcpClientStream.Stream.ReadByte();
            if (version == -1)
            {
                if (!afterHello)
                    _logger.LogWarning(GeneralEventId.Tcp, "Connection closed without any request!");
                return Task.FromResult(false);
            }

            // read request code
            var requestCode = tcpClientStream.Stream.ReadByte();

            switch ((RequestCode)requestCode)
            {
                case RequestCode.Hello:
                    if (afterHello)
                        throw new Exception("Hello request has already been processed!");
                    return ProcessHello(tcpClientStream);

                case RequestCode.TcpDatagramChannel:
                    ProcessTcpDatagramChannel(tcpClientStream);
                    return Task.FromResult(true);

                case RequestCode.TcpProxyChannel:
                    ProcessTcpProxyChannel(tcpClientStream);
                    return Task.FromResult(true);

                default:
                    throw new NotSupportedException("Unknown requestCode!");
            }
        }

        private void ProcessTcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<TcpDatagramChannel>()}");

            // read SessionId
            _logger.LogInformation(GeneralEventId.DatagramChannel, $"Reading the request...");
            var request = StreamUtil.ReadJson<TcpDatagramChannelRequest>(tcpClientStream.Stream);

            // finding session
            using var _scope2 = _logger.BeginScope($"SessionId: {VhLogger.FormatId(request.SessionId)}");
            _logger.LogTrace(GeneralEventId.DatagramChannel, $"SessionId has been readed.");

            try
            {
                var session = _sessionManager.GetSessionById(request.SessionId);

                // send OK reply
                StreamUtil.WriteJson(tcpClientStream.Stream, new SessionResponse() { ResponseCode = ResponseCode.Ok });

                _logger.LogTrace(GeneralEventId.DatagramChannel, $"Creating a channel. ClientId: { VhLogger.FormatId(session.ClientId)}");
                var channel = new TcpDatagramChannel(tcpClientStream);

                // remove all UdpChannel if a tcpDatagram channel is requested
                foreach (var item in session.Tunnel.DatagramChannels.Where(x => x is UdpChannel))
                    session.Tunnel.RemoveChannel(item, false); //don't dispose the session UdpChannel for later use
                session.Tunnel.AddChannel(channel);
            }
            catch (Exception ex)
            {
                if (request.ServerId == _sessionManager.ServerId)
                    WriteChannelResponseException(ex, tcpClientStream.Stream);
                throw;
            }
        }

        private void ProcessTcpProxyChannel(TcpClientStream tcpClientStream)
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<TcpProxyChannel>()}");

            _logger.LogInformation(GeneralEventId.StreamChannel, $"Reading the request...");
            var request = StreamUtil.ReadJson<TcpProxyChannelRequest>(tcpClientStream.Stream);

            // find session
            using var _scope2 = _logger.BeginScope($"SessionId: {VhLogger.FormatId(request.SessionId)}");
            var isRequestedEpException = false;

            try
            {
                var session = _sessionManager.GetSessionById(request.SessionId);

                // connect to requested site
                _logger.LogTrace(GeneralEventId.StreamChannel, $"Connecting to the requested endpoint. RequestedEP: {VhLogger.FormatDns(request.DestinationAddress)}:{request.DestinationPort}");
                var requestedEndPoint = new IPEndPoint(IPAddress.Parse(request.DestinationAddress), request.DestinationPort);

                isRequestedEpException = true;
                var tcpClient2 = _tcpClientFactory.CreateAndConnect(requestedEndPoint);
                isRequestedEpException = false;

                // send response
                var response = new SessionResponse()
                {
                    ResponseCode = ResponseCode.Ok,
                };
                StreamUtil.WriteJson(tcpClientStream.Stream, response);

                // Dispose ssl strean and repalce it with a HeadCryptor
                tcpClientStream.Stream.Dispose();
                tcpClientStream.Stream = StreamHeadCryptor.Create(tcpClientStream.TcpClient.GetStream(),
                    request.CipherKey, null, request.CipherLength);

                // add the connection
                _logger.LogTrace(GeneralEventId.StreamChannel, $"Adding the connection. ClientId: { VhLogger.FormatId(session.ClientId)}, CipherLength: {request.CipherLength}");
                var channel = new TcpProxyChannel(new TcpClientStream(tcpClient2, tcpClient2.GetStream()), tcpClientStream);
                session.Tunnel.AddChannel(channel);
            }
            catch (Exception ex)
            {
                if (request.ServerId == _sessionManager.ServerId)
                    WriteChannelResponseException(ex, tcpClientStream.Stream);

                // simple log; it is not the error caused by VpnHood
                if (isRequestedEpException)
                {
                    VhLogger.Current.LogInformation(GeneralEventId.StreamChannel, $"Could not connect to RequestedEP! {ex.Message}");
                    return;
                }

                // level up the exception
                throw;
            }
        }

        private void WriteChannelResponseException(Exception ex, Stream stream)
        {
            if (ex is SessionException sessionException)
            {
                StreamUtil.WriteJson(stream, new SessionResponse()
                {
                    AccessUsage = sessionException.AccessUsage,
                    ResponseCode = sessionException.ResponseCode,
                    SuppressedBy = sessionException.SuppressedBy,
                    ErrorMessage = sessionException.Message
                });
            }
            else
            {
                StreamUtil.WriteJson(stream, new SessionResponse()
                {
                    ResponseCode = ResponseCode.GeneralError,
                    ErrorMessage = ex.Message
                });
            }
        }

        private async Task<bool> ProcessHello(TcpClientStream tcpClientStream)
        {
            _logger.LogInformation(GeneralEventId.Hello, $"Processing hello request...");
            var request = StreamUtil.ReadJson<HelloRequest>(tcpClientStream.Stream);

            // creating a session
            _logger.LogInformation(GeneralEventId.Hello, $"Creating Session... TokenId: {VhLogger.FormatId(request.TokenId)}, ClientId: {VhLogger.FormatId(request.ClientId)}, ClientVersion: {request.ClientVersion}");
            var clientEp = (IPEndPoint)tcpClientStream.TcpClient.Client.RemoteEndPoint;

            try
            {
                var session = await _sessionManager.CreateSession(request, clientEp.Address);

                // reply hello session
                _logger.LogTrace(GeneralEventId.Hello, $"Replying Hello response. SessionId: {VhLogger.FormatId(session.SessionId)}");
                var helloResponse = new HelloResponse()
                {
                    SessionId = session.SessionId,
                    SessionKey = session.SessionKey,
                    UdpPort = session.UdpChannel.LocalPort,
                    ServerId = _sessionManager.ServerId,
                    SuppressedTo = session.SuppressedTo,
                    AccessUsage = session.AccessController.AccessUsage,
                    ResponseCode = ResponseCode.Ok
                };
                StreamUtil.WriteJson(tcpClientStream.Stream, helloResponse);

                // reuse udp channel
                if (!request.UseUdpChannel)
                {
                    _logger.LogTrace(GeneralEventId.Hello, $"Reusing Hello stream as a {VhLogger.FormatTypeName<TcpDatagramChannel>()}...");
                    return await ProcessRequest(tcpClientStream, true);
                }

                return true;
            }
            catch (SessionException ex)
            {
                // reply error if it is SessionException
                // do not catch other exception and should not reply anything when sesson has not been created
                StreamUtil.WriteJson(tcpClientStream.Stream, new HelloResponse()
                {
                    AccessUsage = ex.AccessUsage,
                    ResponseCode = ex.ResponseCode,
                    ErrorMessage = ex.Message
                });
                throw;
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _tcpListener.Stop();
        }

    }
}