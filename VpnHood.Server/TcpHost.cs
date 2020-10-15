using VpnHood.Logger;
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

namespace VpnHood.Server
{

    class TcpHost : IDisposable
    {
        private readonly TcpListener _tcpListener;
        private readonly X509Certificate2 _certificate;
        private readonly SessionManager _sessionManager;
        private readonly TcpClientFactory _tcpClientFactory;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private ILogger Logger => VpnHood.Logger.Logger.Current;

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
                    tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    var _ = Task.Run(() => ProcessClient(tcpClient));
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

        private void ProcessClient(TcpClient tcpClient)
        {
            using var _ = Logger.BeginScope($"RemoteEp: {tcpClient.Client.RemoteEndPoint}");

            try
            {
                // find certificate by ip 

                // establish SSL
                Logger.LogInformation($"TLS Authenticating. CertSubject: {_certificate.Subject}...");
                var sslStream = new SslStream(tcpClient.GetStream(), true);
                sslStream.AuthenticateAsServer(_certificate, false, true);

                var tcpClientStream = new TcpClientStream(tcpClient, sslStream);
                if (!ProcessRequest(tcpClientStream))
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

        bool ProcessRequest(TcpClientStream tcpClientStream, bool afterHello = false)
        {
            // read version
            Logger.LogTrace($"Waiting for request...");
            var version = tcpClientStream.Stream.ReadByte();
            if (version == -1)
            {
                if (!afterHello)
                    Logger.LogWarning("Connection closed without any request!");
                return false;
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
                    return true;

                case RequestCode.TcpProxyChannel:
                    ProcessTcpProxyChannel(tcpClientStream);
                    return true;

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

            var session = _sessionManager.FindSessionById(sessionId, out SuppressType suppressedBy);

            // send suppressedBy reply
            if (suppressedBy != SuppressType.None)
                Util.Stream_WriteJson(tcpClientStream.Stream, new ChannelResponse() { ResponseCode = ChannelResponse.Code.Suppressed, SuppressedBy = suppressedBy });

            // if invalid session just close connection without sending any data to user. no fingerprint for unknown caller
            if (session == null)
                throw new Exception($"Invalid SessionId! SessionId: {sessionId}");

            // send OK reply
            Util.Stream_WriteJson(tcpClientStream.Stream, new ChannelResponse() { ResponseCode = ChannelResponse.Code.Ok });

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
            var session = _sessionManager.FindSessionById(request.SessionId, out SuppressType suppressedBy);

            // send suppressedBy reply
            if (suppressedBy != SuppressType.None)
            {
                Util.Stream_WriteJson(tcpClientStream.Stream,
                    new ChannelResponse() { ResponseCode = ChannelResponse.Code.Suppressed, SuppressedBy = suppressedBy });
                throw new Exception($"Session {Util.FormatId(request.SessionId)} is suppressedBy {suppressedBy}!");
            }

            // if invalid session just close connection without sending any data to user. no fingerprint for unknown caller
            if (session == null)
                throw new Exception($"Invalid SessionId! SessionId: {request.SessionId}");

            // connect to requested site
            Logger.LogTrace($"Connecting to the requested endpoint. RequestedEP: {request.DestinationAddress}:{request.DestinationPort}");
            var requestedEndPoint = new IPEndPoint(IPAddress.Parse(request.DestinationAddress), request.DestinationPort);
            var tcpClient2 = _tcpClientFactory.CreateAndConnect(requestedEndPoint);

            // send response
            var response = new ChannelResponse()
            {
                ResponseCode = ChannelResponse.Code.Ok,
            };
            Util.Stream_WriteJson(tcpClientStream.Stream, response);

            // add the connection
            Logger.LogTrace($"Adding the connection. ClientId: { Util.FormatId(session.ClientId)}, TlsLength: {request.TlsLength}");
            var channel = new TcpProxyChannel(new TcpClientStream(tcpClient2, tcpClient2.GetStream()), tcpClientStream, request.TlsLength);
            session.Tunnel.AddChannel(channel);
        }

        private bool ProcessHello(TcpClientStream tcpClientStream)
        {
            Logger.LogInformation($"Processing hello request...");
            var helloRequest = Util.Stream_ReadJson<HelloRequest>(tcpClientStream.Stream);

            // creating a session
            Logger.LogInformation($"Creating Session... TokenId: {helloRequest.TokenId}, ClientId: {Util.FormatId(helloRequest.ClientId)}");
            var session = _sessionManager.CreateSession(helloRequest, (IPEndPoint)tcpClientStream.TcpClient.Client.RemoteEndPoint);

            // reply hello
            Logger.LogTrace($"Replying Hello response...");
            var helloResponse = new HelloResponse()
            {
                SessionId = session.SessionId,
                SuppressedTo = session.SuppressedTo
            };
            Util.Stream_WriteJson(tcpClientStream.Stream, helloResponse);

            Logger.LogTrace($"Reusing Hello stream...");
            return ProcessRequest(tcpClientStream, true);
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _tcpListener.Stop();
        }

    }
}