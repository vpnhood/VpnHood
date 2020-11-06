using VpnHood.Loggers;
using VpnHood.Messages;
using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Client;

namespace VpnHood.Server
{

    class TcpHost : IDisposable
    {
        private readonly TcpListener _tcpListener;
        private readonly X509Certificate2 _certificate;
        private readonly SessionManager _sessionManager;
        private readonly TcpClientFactory _tcpClientFactory;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private ILogger Logger => Loggers.Logger.Current;

        public TcpHost(IPEndPoint endPoint, X509Certificate2 certificate, SessionManager sessionManager, TcpClientFactory tcpClientFactory)
        {
            if (endPoint == null) throw new ArgumentNullException(nameof(endPoint));
            _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
            _tcpListener = new TcpListener(endPoint);
            _sessionManager = sessionManager;
            _tcpClientFactory = tcpClientFactory;
        }

        public IPEndPoint LocalEndPoint => (IPEndPoint)_tcpListener.LocalEndpoint;

        public void Start()
        {
            using var _ = Logger.BeginScope($"{Util.FormatTypeName<TcpHost>()}");
            var tasks = new ConcurrentDictionary<Task, int>();

            Logger.LogInformation($"Start listening on {_tcpListener.LocalEndpoint}...");
            _tcpListener.Start();

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
                    Logger.LogError($"{ex.Message}");
            }
            finally
            {
                Logger.LogInformation($"{Util.FormatTypeName(this)} Listener has been closed.");
            }

        }

        private async Task ProcessClient(TcpClient tcpClient)
        {
            using var _ = Logger.BeginScope($"RemoteEp: {tcpClient.Client.RemoteEndPoint}");

            try
            {
                // find certificate by ip 

                // establish SSL
                Logger.LogInformation($"TLS Authenticating. CertSubject: {_certificate.Subject}...");
                var sslStream = new SslStream(tcpClient.GetStream(), true);
                await sslStream.AuthenticateAsServerAsync(_certificate, false, true);

                var tcpClientStream = new TcpClientStream(tcpClient, sslStream);
                if (!await ProcessRequestTask(tcpClientStream))
                {
                    tcpClientStream.Dispose();
                    Logger.LogTrace($"Connection has been closed.");
                }
            }
            catch (Exception ex)
            {
                tcpClient.Dispose();

                // logging
                if (!(ex is ObjectDisposedException))
                    Logger.LogError($"{ex.Message}");
                else
                    Logger.LogTrace($"Connection has been closed.");
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
            Logger.LogTrace($"Waiting for request...");
            var version = tcpClientStream.Stream.ReadByte();
            if (version == -1)
            {
                if (!afterHello)
                    Logger.LogWarning("Connection closed without any request!");
                return Task.FromResult(false);
            }
            if (version != 1) throw new NotSupportedException($"Unsupported version in TCP stream. Version: {version}");

            // read request code
            var requestCode = tcpClientStream.Stream.ReadByte();
            if (version == -1) throw new Exception("Could not read client request code!");

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
            using var _ = Logger.BeginScope($"{Util.FormatTypeName<TcpDatagramChannel>()}");

            // read SessionId
            Logger.LogTrace($"Reading SessionId...");
            var buffer = Util.Stream_ReadWaitForFill(tcpClientStream.Stream, 8);
            if (buffer == null) throw new Exception("Could not read Session Id!");

            // finding session
            var sessionId = BitConverter.ToUInt64(buffer);
            using var _scope2 = Logger.BeginScope($"SessionId: {Util.FormatId(sessionId)}");
            Logger.LogTrace($"SessionId has been readed.");

            var session = GetSessionById(sessionId, tcpClientStream.Stream);

            // send OK reply
            Util.Stream_WriteJson(tcpClientStream.Stream, new ChannelResponse() { ResponseCode = ResponseCode.Ok });

            Logger.LogTrace($"Creating a channel. ClientId: { Util.FormatId(session.ClientId)}");
            var channel = new TcpDatagramChannel(tcpClientStream);
            session.Tunnel.AddChannel(channel);
        }

        private void ProcessTcpProxyChannel(TcpClientStream tcpClientStream)
        {
            using var _ = Logger.BeginScope($"{Util.FormatTypeName<TcpProxyChannel>()}");

            Logger.LogInformation($"Reading the request...");
            var request = Util.Stream_ReadJson<TcpProxyChannelRequest>(tcpClientStream.Stream);

            // find session
            using var _scope2 = Logger.BeginScope($"SessionId: {Util.FormatId(request.SessionId)}");
            var session = GetSessionById(request.SessionId, tcpClientStream.Stream);

            // connect to requested site
            Logger.LogTrace($"Connecting to the requested endpoint. RequestedEP: {request.DestinationAddress}:{request.DestinationPort}");
            var requestedEndPoint = new IPEndPoint(IPAddress.Parse(request.DestinationAddress), request.DestinationPort);
            var tcpClient2 = _tcpClientFactory.CreateAndConnect(requestedEndPoint);

            // send response
            var response = new ChannelResponse()
            {
                ResponseCode = ResponseCode.Ok,
            };
            Util.Stream_WriteJson(tcpClientStream.Stream, response);

            // Dispose ssl strean and repalce it with a HeadCryptor
            tcpClientStream.Stream.Dispose();
            tcpClientStream.Stream = StreamHeadCryptor.CreateAesCryptor(tcpClientStream.TcpClient.GetStream(),
                session.AccessController.Access.Secret, request.CipherSault, request.CipherLength);

            // add the connection
            Logger.LogTrace($"Adding the connection. ClientId: { Util.FormatId(session.ClientId)}, CipherLength: {request.CipherLength}");
            var channel = new TcpProxyChannel(new TcpClientStream(tcpClient2, tcpClient2.GetStream()), tcpClientStream);
            session.Tunnel.AddChannel(channel);
        }

        private Session GetSessionById(ulong sessionId, Stream stream)
        {
            try
            {
                return _sessionManager.GetSessionById(sessionId);
            }
            catch (SessionException ex)
            {
                // reply error
                Util.Stream_WriteJson(stream, new ChannelResponse()
                {
                    AccessUsage = ex.AccessUsage,
                    ResponseCode = ex.ResponseCode,
                    SuppressedBy = ex.SuppressedBy,
                    ErrorMessage = ex.Message
                });
                throw;
            }
        }

        private async Task<bool> ProcessHello(TcpClientStream tcpClientStream)
        {
            Logger.LogInformation($"Processing hello request...");
            var helloRequest = Util.Stream_ReadJson<HelloRequest>(tcpClientStream.Stream);

            // creating a session
            Logger.LogInformation($"Creating Session... TokenId: {helloRequest.TokenId}, ClientId: {Util.FormatId(helloRequest.ClientId)}");
            var clientEp = (IPEndPoint)tcpClientStream.TcpClient.Client.RemoteEndPoint;

            try
            {
                var session = await _sessionManager.CreateSession(helloRequest, clientEp.Address);

                // reply hello session
                Logger.LogTrace($"Replying Hello response. SessionId: {session.SessionId}");
                var helloResponse = new HelloResponse()
                {
                    SessionId = session.SessionId,
                    SuppressedTo = session.SuppressedTo
                };
                Util.Stream_WriteJson(tcpClientStream.Stream, helloResponse);

                Logger.LogTrace($"Reusing Hello stream...");
                return await ProcessRequest(tcpClientStream, true);
            }
            catch (SessionException ex)
            {
                // reply error
                Util.Stream_WriteJson(tcpClientStream.Stream, new ChannelResponse()
                {
                    AccessUsage = ex.AccessUsage,
                    ResponseCode = ex.ResponseCode,
                    SuppressedBy = ex.SuppressedBy,
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