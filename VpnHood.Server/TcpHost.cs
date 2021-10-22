using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;
using System.Collections.Generic;

namespace VpnHood.Server
{
    internal class TcpHost : IDisposable
    {
        private const int ServerProtocolVersion = 2;
        private readonly TimeSpan _remoteHostTimeout = TimeSpan.FromSeconds(60);
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly SessionManager _sessionManager;
        private readonly SocketFactory _socketFactory;
        private readonly SslCertificateManager _sslCertificateManager;
        private readonly List<TcpListener> _tcpListeners = new();
        public bool IsDisposed { get; private set; }
        private Task? _startTask;

        public TcpHost(SessionManager sessionManager, SslCertificateManager sslCertificateManager, SocketFactory socketFactory)
        {
            _sslCertificateManager = sslCertificateManager ?? throw new ArgumentNullException(nameof(sslCertificateManager));
            _sessionManager = sessionManager;
            _socketFactory = socketFactory;
        }

        public int OrgStreamReadBufferSize { get; set; } = TunnelUtil.StreamBufferSize;
        public int TunnelStreamReadBufferSize { get; set; } = TunnelUtil.StreamBufferSize;
        public bool IsStarted => _cancellationTokenSource != null;

        public void Start(IPEndPoint[] tcpEndPoints)
        {
            if (IsStarted) throw new Exception($"{nameof(TcpHost)} is already Started!");
            if (tcpEndPoints.Length == 0) throw new Exception("No TcpEndPoint has been configured!");

            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            var tasks = new List<Task>();
            lock (_tcpListeners)
            {
                foreach (var tcpEndPoint in tcpEndPoints)
                {
                    VhLogger.Instance.LogInformation($"Start listening on {VhLogger.Format(tcpEndPoint)}");
                    cancellationToken.ThrowIfCancellationRequested();
                    var tcpListener = new TcpListener(tcpEndPoint);
                    tcpListener.Start();
                    _tcpListeners.Add(tcpListener);
                    tasks.Add(ListenTask(tcpListener, cancellationToken));
                }
            }

            _startTask = Task.WhenAll(tasks);
        }

        public async Task Stop()
        {
            _cancellationTokenSource?.Cancel();
            _sslCertificateManager.ClearCache();

            lock (_tcpListeners)
            {
                foreach (var tcpListener in _tcpListeners)
                    tcpListener.Stop();
                _tcpListeners.Clear();
            }

            if (_startTask != null)
            {
                await _startTask;
                _startTask = null;
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private async Task ListenTask(TcpListener tcpListener, CancellationToken cancellationToken)
        {
            try
            {
                // Listening for new connection
                while (!cancellationToken.IsCancellationRequested)
                {
                    var tcpClient = await tcpListener.AcceptTcpClientAsync();
                    tcpClient.NoDelay = true;

                    // create cancellation token
                    _ = ProcessClient(tcpClient, cancellationToken);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
            }
            catch (Exception ex)
            {
                if (ex is not ObjectDisposedException)
                    VhLogger.Instance.LogError($"{ex.Message}");
            }
            finally
            {
                tcpListener.Stop();
                VhLogger.Instance.LogInformation($"{VhLogger.FormatTypeName(this)} Listener has been closed.");
            }
        }

        private async Task ProcessClient(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            using var scope = VhLogger.Instance.BeginScope($"RemoteEp: {tcpClient.Client.RemoteEndPoint}");

            // add timeout to cancellationToken
            using var timeoutCt = new CancellationTokenSource(_remoteHostTimeout);
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, cancellationToken);
            cancellationToken = cancellationTokenSource.Token;
            TcpClientStream? tcpClientStream = null;

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
                await ProcessRequest(tcpClientStream, cancellationToken);
            }
            catch (SessionException ex) when (tcpClientStream != null)
            {
                // reply error if it is SessionException
                // do not catch other exception and should not reply anything when session has not been created
                await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, new ResponseBase(ex.SessionResponse),
                    cancellationToken);

                tcpClientStream.Dispose();
                tcpClient.Dispose();
                VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"Connection has been closed. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                tcpClientStream?.Dispose();
                tcpClient.Dispose();

                // logging
                if (ex is ObjectDisposedException)
                    VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "Connection has been closed.");
                else
                    VhLogger.Instance.LogError($"{ex}");
            }
        }

        private async Task ProcessRequest(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
        {
            // read version
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "Waiting for request...");
            var buffer = new byte[2];
            var res = await tcpClientStream.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (res != buffer.Length)
                throw new Exception("Connection closed before receiving any request!");

            // check request version
            var version = buffer[0];
            if (version != 1)
                throw new NotSupportedException("The request version is not supported!");

            // read request code
            var requestCode = (RequestCode)buffer[1];
            switch (requestCode)
            {
                case RequestCode.Hello:
                    await ProcessHello(tcpClientStream, cancellationToken);
                    break;

                case RequestCode.TcpDatagramChannel:
                    await ProcessTcpDatagramChannel(tcpClientStream, cancellationToken);
                    break;

                case RequestCode.TcpProxyChannel:
                    await ProcessTcpProxyChannel(tcpClientStream, cancellationToken);
                    break;

                case RequestCode.UdpChannel:
                    await ProcessUdpChannel(tcpClientStream, cancellationToken);
                    break;

                default:
                    throw new NotSupportedException("Unknown requestCode!");
            }
        }

        private async Task ProcessHello(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
        {
            var clientEndPoint = (IPEndPoint)tcpClientStream.TcpClient.Client.RemoteEndPoint;
            var requestEndPoint = (IPEndPoint)tcpClientStream.TcpClient.Client.LocalEndPoint;
            VhLogger.Instance.LogInformation(GeneralEventId.Hello, $"Processing hello request... ClientEp: {VhLogger.Format(clientEndPoint)}");
            var request = await StreamUtil.ReadJsonAsync<HelloRequest>(tcpClientStream.Stream, cancellationToken);

            // check client version; actually it should be removed in future to preserver server anonymity
            if (request.ClientInfo == null || request.ClientInfo.ProtocolVersion < 2)
                throw new SessionException(SessionErrorCode.UnsupportedClient, "This client is outdated and not supported anymore! Please update your app.");

            // creating a session
            VhLogger.Instance.LogInformation(GeneralEventId.Hello,
                $"Creating Session... TokenId: {VhLogger.FormatId(request.TokenId)}, ClientId: {VhLogger.FormatId(request.ClientInfo.ClientId)}, ClientVersion: {request.ClientInfo.ClientVersion}");
            var sessionResponse = await _sessionManager.CreateSession(request, requestEndPoint, clientEndPoint.Address);
            var session = _sessionManager.GetSessionById(sessionResponse.SessionId) ??
                          throw new InvalidOperationException("Session is lost!");
            session.UseUdpChannel = request.UseUdpChannel;

            // reply hello session
            VhLogger.Instance.LogTrace(GeneralEventId.Hello,
                $"Replying Hello response. SessionId: {VhLogger.FormatSessionId(sessionResponse.SessionId)}");
            var helloResponse = new HelloResponse(sessionResponse)
            {
                SessionId = sessionResponse.SessionId,
                SessionKey = sessionResponse.SessionKey,
                UdpKey = session.UdpChannel?.Key,
                UdpPort = session.UdpChannel?.LocalPort ?? 0,
                ServerVersion = _sessionManager.ServerVersion,
                ServerProtocolVersion = ServerProtocolVersion,
                SuppressedTo = sessionResponse.SuppressedTo,
                AccessUsage = sessionResponse.AccessUsage,
                MaxDatagramChannelCount = session.Tunnel.MaxDatagramChannelCount,
                ClientPublicAddress = clientEndPoint.Address,
                ErrorCode = SessionErrorCode.Ok
            };
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, helloResponse, cancellationToken);
            tcpClientStream.Dispose();
        }

        private async Task ProcessUdpChannel(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
        {
            VhLogger.Instance.LogTrace(GeneralEventId.Udp,
                $"Reading the {VhLogger.FormatTypeName<TcpDatagramChannel>()} request...");
            var request = await StreamUtil.ReadJsonAsync<UdpChannelRequest>(tcpClientStream.Stream, cancellationToken);

            // finding session
            var session = await _sessionManager.GetSession(request, tcpClientStream.LocalEndPoint,
                tcpClientStream.RemoteEndPoint.Address);
            session.UseUdpChannel = true;
            if (session.UdpChannel == null)
                throw new InvalidOperationException($"{nameof(session.UdpChannel)} is not initialized!");

            // send OK reply
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, new UdpChannelResponse(session.SessionResponse)
            {
                UdpKey = session.UdpChannel.Key,
                UdpPort = session.UdpChannel.LocalPort
            }, cancellationToken);

            tcpClientStream.Dispose();
        }

        private async Task ProcessTcpDatagramChannel(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
        {
            VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel,
                $"Reading the {VhLogger.FormatTypeName<TcpDatagramChannel>()} request...");
            var request =
                await StreamUtil.ReadJsonAsync<TcpDatagramChannelRequest>(tcpClientStream.Stream, cancellationToken);

            // finding session
            using var _ = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
            var session = await _sessionManager.GetSession(request, tcpClientStream.LocalEndPoint,
                tcpClientStream.RemoteEndPoint.Address);

            // send OK reply
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, session.SessionResponse, cancellationToken);

            // add channel
            VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel,
                $"Creating a {nameof(TcpDatagramChannel)} channel. SessionId: {VhLogger.FormatId(session.SessionId)}");
            var channel = new TcpDatagramChannel(tcpClientStream);

            // disable udpChannel
            session.UseUdpChannel = false;
            session.Tunnel.AddChannel(channel);
        }

        private async Task ProcessTcpProxyChannel(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.StreamChannel, $"Reading the {VhLogger.FormatTypeName<TcpProxyChannel>()} request...");
            var request = await StreamUtil.ReadJsonAsync<TcpProxyChannelRequest>(tcpClientStream.Stream, cancellationToken);

            // find session
            using var _ = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatId(request.SessionId)}");
            var isRequestedEpException = false;
            try
            {
                var session = await _sessionManager.GetSession(request, tcpClientStream.LocalEndPoint,
                    tcpClientStream.RemoteEndPoint.Address);

                // connect to requested site
                VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel,
                    $"Connecting to the requested endpoint. RequestedEP: {VhLogger.Format(request.DestinationEndPoint)}");

                var tcpClient2 = _socketFactory.CreateTcpClient(request.DestinationEndPoint.AddressFamily);
                tcpClient2.NoDelay = true;
                Util.TcpClient_SetKeepAlive(tcpClient2, true);

                isRequestedEpException = true;
                await Util.RunTask(tcpClient2.ConnectAsync(request.DestinationEndPoint.Address, request.DestinationEndPoint.Port),
                    _remoteHostTimeout, cancellationToken);
                isRequestedEpException = false;

                // send response
                await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, session.SessionResponse, cancellationToken);

                // Dispose ssl stream and replace it with a Head-Cryptor
                await tcpClientStream.Stream.DisposeAsync();
                tcpClientStream.Stream = StreamHeadCryptor.Create(tcpClientStream.TcpClient.GetStream(),
                    request.CipherKey, null, request.CipherLength);

                // add the connection
                VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel,
                    $"Adding a {nameof(TcpProxyChannel)}. SessionId: {VhLogger.FormatId(session.SessionId)}, CipherLength: {request.CipherLength}");
                Util.TcpClient_SetKeepAlive(tcpClientStream.TcpClient, true);
                var channel = new TcpProxyChannel(new TcpClientStream(tcpClient2, tcpClient2.GetStream()),
                    tcpClientStream,
                    OrgStreamReadBufferSize, TunnelStreamReadBufferSize);
                session.Tunnel.AddChannel(channel);
            }
            catch (Exception ex) when (isRequestedEpException)
            {
                throw new SessionException(SessionErrorCode.GeneralError, ex.Message);
            }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Stop().Wait();
        }
    }
}