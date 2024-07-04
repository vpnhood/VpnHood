using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Exceptions;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Channels;
using VpnHood.Tunneling.Channels.Streams;
using VpnHood.Tunneling.ClientStreams;
using VpnHood.Tunneling.Messaging;
using VpnHood.Tunneling.Utils;
using Exception = System.Exception;

namespace VpnHood.Server;

public class ServerHost : IAsyncDisposable, IJob
{
    private readonly HashSet<IClientStream> _clientStreams = [];
    private const int ServerProtocolVersion = 5;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SessionManager _sessionManager;
    private readonly List<TcpListener> _tcpListeners;
    private readonly List<UdpChannelTransmitter> _udpChannelTransmitters = [];
    private readonly List<Task> _tcpListenerTasks = [];
    private bool _disposed;

    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(5));
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? NetFilterPacketCaptureIncludeIpRanges { get; set; }
    public IpRange[]? NetFilterIncludeIpRanges { get; set; }
    public IPAddress[]? DnsServers { get; set; }
    public CertificateHostName[] Certificates { get; private set; } = [];
    public IPEndPoint[] TcpEndPoints => _tcpListeners.Select(x => (IPEndPoint)x.LocalEndpoint).ToArray();

    public ServerHost(SessionManager sessionManager)
    {
        _tcpListeners = [];
        _sessionManager = sessionManager;
        JobRunner.Default.Add(this);
    }

    public async Task Configure(IPEndPoint[] tcpEndPoints, IPEndPoint[] udpEndPoints,
        IPAddress[]? dnsServers, X509Certificate2[] certificates)
    {
        if (VhUtil.IsNullOrEmpty(certificates))
            throw new ArgumentNullException(nameof(certificates), "No certificate has been configured.");

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // wait for last configure to finish
        using var lockResult = await _configureLock.LockAsync(_cancellationTokenSource.Token).VhConfigureAwait();

        // reconfigure
        DnsServers = dnsServers;
        Certificates = certificates.Select(x => new CertificateHostName(x)).ToArray();

        // Configure
        await Task.WhenAll(ConfigureUdpListeners(udpEndPoints), ConfigureTcpListeners(tcpEndPoints)).VhConfigureAwait();
        _tcpListenerTasks.RemoveAll(x => x.IsCompleted);
    }

    private readonly AsyncLock _configureLock = new();
    private Task ConfigureUdpListeners(IPEndPoint[] udpEndPoints)
    {
        // UDP port zero must be specified in preparation
        if (udpEndPoints.Any(x => x.Port == 0))
            throw new InvalidOperationException("UDP port has not been specified.");

        // stop listeners that are not in the list
        foreach (var udpChannelTransmitter in _udpChannelTransmitters
                     .Where(x => !udpEndPoints.Contains(x.LocalEndPoint)).ToArray())
        {
            VhLogger.Instance.LogInformation("Stop listening on UdpEndPoint: {UdpEndPoint}", VhLogger.Format(udpChannelTransmitter.LocalEndPoint));
            udpChannelTransmitter.Dispose();
            _udpChannelTransmitters.Remove(udpChannelTransmitter);
        }

        // start new listeners
        foreach (var udpEndPoint in udpEndPoints)
        {
            // ignore already listening
            if (_udpChannelTransmitters.Any(x => x.LocalEndPoint.Equals(udpEndPoint)))
                continue;

            // report if port is set
            if (udpEndPoint.Port == 0)
                VhLogger.Instance.LogInformation("Start listening on UdpEndPoint: {UdpEndPoint}", VhLogger.Format(udpEndPoint));

            try
            {
                var udpClient = new UdpClient(udpEndPoint);
                var udpChannelTransmitter = new ServerUdpChannelTransmitter(udpClient, _sessionManager);
                _udpChannelTransmitters.Add(udpChannelTransmitter);

                if (udpEndPoint.Port == 0)
                    VhLogger.Instance.LogInformation("Started listening on UdpEndPoint: {UdpEndPoint}", VhLogger.Format(udpChannelTransmitter.LocalEndPoint));
            }
            catch (Exception ex)
            {
                ex.Data.Add("UdpEndPoint", udpEndPoint);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    private Task ConfigureTcpListeners(IPEndPoint[] tcpEndPoints)
    {
        if (tcpEndPoints.Length == 0)
            throw new ArgumentNullException(nameof(tcpEndPoints), "No TcpEndPoint has been configured.");

        // start TCPs
        // stop listeners that are not in the new list
        foreach (var tcpListener in _tcpListeners
                     .Where(x => !tcpEndPoints.Contains((IPEndPoint)x.LocalEndpoint)).ToArray())
        {
            VhLogger.Instance.LogInformation("Stop listening on TcpEndPoint: {TcpEndPoint}", VhLogger.Format(tcpListener.LocalEndpoint));
            tcpListener.Stop();
            _tcpListeners.Remove(tcpListener);
        }

        foreach (var tcpEndPoint in tcpEndPoints)
        {
            // check already listening
            if (_tcpListeners.Any(x => x.LocalEndpoint.Equals(tcpEndPoint)))
                continue;

            try
            {
                VhLogger.Instance.LogInformation("Start listening on TcpEndPoint: {TcpEndPoint}", VhLogger.Format(tcpEndPoint));
                var tcpListener = new TcpListener(tcpEndPoint);
                tcpListener.Start();
                _tcpListeners.Add(tcpListener);
                _tcpListenerTasks.Add(ListenTask(tcpListener, _cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogInformation("Error listening on TcpEndPoint: {TcpEndPoint}", VhLogger.Format(tcpEndPoint));
                ex.Data.Add("TcpEndPoint", tcpEndPoint);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    private async Task ListenTask(TcpListener tcpListener, CancellationToken cancellationToken)
    {
        var localEp = (IPEndPoint)tcpListener.LocalEndpoint;
        var errorCounter = 0;
        const int maxErrorCount = 200;

        // Listening for new connection
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await tcpListener.AcceptTcpClientAsync().VhConfigureAwait();
                if (_disposed)
                    throw new ObjectDisposedException("ServerHost has been stopped.");

                VhUtil.ConfigTcpClient(tcpClient,
                    _sessionManager.SessionOptions.TcpKernelSendBufferSize,
                    _sessionManager.SessionOptions.TcpKernelReceiveBufferSize);

                // config tcpClient
                _ = ProcessTcpClient(tcpClient, cancellationToken);
                errorCounter = 0;
            }
            catch (Exception) when (!tcpListener.Server.IsBound)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                errorCounter++;
            }
            catch (ObjectDisposedException)
            {
                errorCounter++;
            }
            catch (Exception ex)
            {
                errorCounter++;
                if (errorCounter > maxErrorCount)
                {
                    VhLogger.Instance.LogError(
                        "Too many unexpected errors in AcceptTcpClient. Stopping the Listener... LocalEndPint: {LocalEndPint}",
                        tcpListener.LocalEndpoint);
                    break;
                }

                VhLogger.Instance.LogError(GeneralEventId.Tcp, ex,
                    "ServerHost could not AcceptTcpClient. LocalEndPint: {LocalEndPint}, ErrorCounter: {ErrorCounter}",
                    tcpListener.LocalEndpoint, errorCounter);
            }
        }

        tcpListener.Stop();
        VhLogger.Instance.LogInformation("TCP Listener has been stopped. LocalEp: {LocalEp}", VhLogger.Format(localEp));
    }

    private async Task<SslStream> AuthenticateAsServerAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "TLS Authenticating...");
            var sslStream = new SslStream(stream, true);
            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = false,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    ServerCertificateSelectionCallback = ServerCertificateSelectionCallback
                },
                cancellationToken)
                .VhConfigureAwait();
            return sslStream;
        }
        catch (Exception ex)
        {
            throw new TlsAuthenticateException("TLS Authentication error.", ex);
        }
    }

    private X509Certificate ServerCertificateSelectionCallback(object sender, string hostname)
    {
        var certificate = Certificates.SingleOrDefault(x => x.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
            ?? Certificates.First();

        return certificate.Certificate;
    }

    private bool CheckApiKeyAuthorization(string authorization)
    {
        var parts = authorization.Split(' ');
        return
            parts.Length >= 2 &&
            parts[0].Equals("ApiKey", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals(_sessionManager.ApiKey, StringComparison.OrdinalIgnoreCase) &&
            parts[1] == _sessionManager.ApiKey;
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "Waiting for request...");
        var streamId = Guid.NewGuid() + ":incoming";

        // Version 2 is HTTP and starts with POST
        try
        {
            var headers =
                await HttpUtil.ParseHeadersAsync(sslStream, cancellationToken).VhConfigureAwait()
                ?? throw new Exception("Connection has been closed before receiving any request.");

            // int.TryParse(headers.GetValueOrDefault("X-Version", "0"), out var xVersion);
            Enum.TryParse<BinaryStreamType>(headers.GetValueOrDefault("X-BinaryStream", ""), out var binaryStreamType);
            bool.TryParse(headers.GetValueOrDefault("X-Buffered", "true"), out var useBuffer);
            var authorization = headers.GetValueOrDefault("Authorization", string.Empty);

            // read api key
            if (!CheckApiKeyAuthorization(authorization))
            {
                // process hello without api key
                if (authorization != "ApiKey")
                    throw new UnauthorizedAccessException();

                await sslStream.WriteAsync(HttpResponseBuilder.Unauthorized(), cancellationToken).VhConfigureAwait();
                return new TcpClientStream(tcpClient, sslStream, streamId);
            }

            // use binary stream only for authenticated clients
            await sslStream.WriteAsync(HttpResponseBuilder.Ok(), cancellationToken).VhConfigureAwait();

            switch (binaryStreamType)
            {
                case BinaryStreamType.Standard:
                    return new TcpClientStream(tcpClient, new BinaryStreamStandard(tcpClient.GetStream(), streamId, useBuffer), streamId, ReuseClientStream);

                case BinaryStreamType.None:
                    return new TcpClientStream(tcpClient, sslStream, streamId);

                case BinaryStreamType.Unknown:
                default:
                    throw new NotSupportedException("Unknown BinaryStreamType");
            }
        }
        catch (Exception ex)
        {
            //always return BadRequest 
            if (!VhUtil.IsTcpClientHealthy(tcpClient)) throw;
            var response = ex is UnauthorizedAccessException ? HttpResponseBuilder.Unauthorized() : HttpResponseBuilder.BadRequest();
            await sslStream.WriteAsync(response, cancellationToken).VhConfigureAwait();
            throw;
        }
    }

    private async Task ProcessTcpClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        // add timeout to cancellationToken
        using var timeoutCt = new CancellationTokenSource(_sessionManager.SessionOptions.TcpReuseTimeoutValue);
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, cancellationToken);
        cancellationToken = cancellationTokenSource.Token;

        IClientStream? clientStream = null;
        try
        {
            // establish SSL
            var sslStream = await AuthenticateAsServerAsync(tcpClient.GetStream(), cancellationToken).VhConfigureAwait();

            // create client stream
            clientStream = await CreateClientStream(tcpClient, sslStream, cancellationToken).VhConfigureAwait();
            lock (_clientStreams) _clientStreams.Add(clientStream);

            await ProcessClientStream(clientStream, cancellationToken).VhConfigureAwait();
        }
        catch (TlsAuthenticateException ex) when (ex.InnerException is OperationCanceledException)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Tls, "Client TLS authentication has been canceled.");
            tcpClient.Dispose();
        }
        catch (TlsAuthenticateException ex)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Tls, ex, "Error in Client TLS authentication.");
            tcpClient.Dispose();
        }
        catch (Exception ex)
        {
            if (ex is ISelfLog loggable) loggable.Log();
            else VhLogger.LogError(GeneralEventId.Request, ex,
                "ServerHost could not process this request. ClientStreamId: {ClientStreamId}", clientStream?.ClientStreamId);

            if (clientStream != null) await clientStream.DisposeAsync(false).VhConfigureAwait();
            tcpClient.Dispose();
        }
    }

    private async Task ReuseClientStream(IClientStream clientStream)
    {
        lock (_clientStreams) _clientStreams.Add(clientStream);
        using var timeoutCt = new CancellationTokenSource(_sessionManager.SessionOptions.TcpReuseTimeoutValue);
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, _cancellationTokenSource.Token);
        var cancellationToken = cancellationTokenSource.Token;

        // don't add new client in disposing
        if (cancellationToken.IsCancellationRequested)
        {
            _ = clientStream.DisposeAsync(false);
            return;
        }

        // process incoming client
        try
        {
            VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                "ServerHost.ReuseClientStream: A shared ClientStream is pending for reuse. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);

            await ProcessClientStream(clientStream, cancellationToken).VhConfigureAwait();

            VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                "ServerHost.ReuseClientStream: A shared ClientStream has been reused. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.Tcp, ex,
                "ServerHost.ReuseClientStream: Could not reuse a ClientStream. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);

            await clientStream.DisposeAsync(false).VhConfigureAwait();
        }
    }

    private async Task ProcessClientStream(IClientStream clientStream, CancellationToken cancellationToken)
    {
        using var scope = VhLogger.Instance.BeginScope($"RemoteEp: {VhLogger.Format(clientStream.IpEndPointPair.RemoteEndPoint)}");
        try
        {
            await ProcessRequest(clientStream, cancellationToken).VhConfigureAwait();
        }
        catch (SessionException ex)
        {
            // reply the error to caller if it is SessionException
            // Should not reply anything when user is unknown
            await StreamUtil.WriteJsonAsync(clientStream.Stream, ex.SessionResponse, cancellationToken).VhConfigureAwait();

            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(ex.SessionResponse.ErrorCode == SessionErrorCode.GeneralError ? GeneralEventId.Tcp : GeneralEventId.Session, ex,
                    "Could not process the request. SessionErrorCode: {SessionErrorCode}", ex.SessionResponse.ErrorCode);

            await clientStream.DisposeAsync().VhConfigureAwait();
        }
        catch (Exception ex) when (VhLogger.IsSocketCloseException(ex))
        {
            VhLogger.LogError(GeneralEventId.Tcp, ex,
                "Connection has been closed. ClientStreamId: {ClientStreamId}.",
                clientStream.ClientStreamId);

            await clientStream.DisposeAsync().VhConfigureAwait();
        }
        catch (Exception ex)
        {
            // return 401 for ANY non SessionException to keep server's anonymity
            await clientStream.Stream.WriteAsync(HttpResponseBuilder.Unauthorized(), cancellationToken).VhConfigureAwait();

            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(GeneralEventId.Tcp, ex,
                    "Could not process the request and return 401. ClientStreamId: {ClientStreamId}", clientStream.ClientStreamId);

            await clientStream.DisposeAsync(false).VhConfigureAwait();
        }
        finally
        {
            lock (_clientStreams)
                _clientStreams.Remove(clientStream);
        }
    }

    private async Task ProcessRequest(IClientStream clientStream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];

        // read request version
        var rest = await clientStream.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).VhConfigureAwait();
        if (rest == 0)
            throw new Exception("ClientStream has been closed before receiving any request.");

        var version = buffer[0];
        if (version != 1)
            throw new NotSupportedException($"The request version is not supported. Version: {version}");

        // read request code
        var res = await clientStream.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).VhConfigureAwait();
        if (res == 0)
            throw new Exception("ClientStream has been closed before receiving any request.");

        var requestCode = (RequestCode)buffer[0];
        switch (requestCode)
        {
            case RequestCode.ServerStatus:
                await ProcessServerStatus(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.SessionStatus:
                await ProcessSessionStatus(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.Hello:
                await ProcessHello(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.TcpDatagramChannel:
                await ProcessTcpDatagramChannel(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.StreamProxyChannel:
                await ProcessStreamProxyChannel(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.UdpPacket:
                await ProcessUdpPacketRequest(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.AdReward:
                await ProcessAdRewardRequest(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.Bye:
                await ProcessBye(clientStream, cancellationToken).VhConfigureAwait();
                break;

            default:
                throw new NotSupportedException($"Unknown requestCode. requestCode: {requestCode}");
        }
    }

    private static async Task<T> ReadRequest<T>(IClientStream clientStream, CancellationToken cancellationToken) where T : ClientRequest
    {
        // reading request
        VhLogger.Instance.LogTrace(GeneralEventId.Session,
            "Processing a request. RequestType: {RequestType}.",
            VhLogger.FormatType<T>());

        var request = await StreamUtil.ReadJsonAsync<T>(clientStream.Stream, cancellationToken).VhConfigureAwait();
        request.RequestId = request.RequestId.Replace(":client", ":server");
        clientStream.ClientStreamId = request.RequestId;

        VhLogger.Instance.LogTrace(GeneralEventId.Session,
            "Request has been read. RequestType: {RequestType}. RequestId: {RequestId}",
            VhLogger.FormatType<T>(), request.RequestId);

        return request;
    }

    private async Task ProcessHello(IClientStream clientStream, CancellationToken cancellationToken)
    {
        var ipEndPointPair = clientStream.IpEndPointPair;

        // reading request
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Processing hello request... ClientIp: {ClientIp}",
            VhLogger.Format(ipEndPointPair.RemoteEndPoint.Address));

        var request = await ReadRequest<HelloRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // creating a session
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Creating a session... TokenId: {TokenId}, ClientId: {ClientId}, ClientVersion: {ClientVersion}, UserAgent: {UserAgent}",
            VhLogger.FormatId(request.TokenId), VhLogger.FormatId(request.ClientInfo.ClientId), request.ClientInfo.ClientVersion, request.ClientInfo.UserAgent);
        var sessionResponse = await _sessionManager.CreateSession(request, ipEndPointPair).VhConfigureAwait();
        var session = _sessionManager.GetSessionById(sessionResponse.SessionId) ?? throw new InvalidOperationException("Session is lost!");

        // check client version; unfortunately it must be after CreateSession to preserve server anonymity
        if (request.ClientInfo == null || request.ClientInfo.ProtocolVersion < 4)
            throw new ServerSessionException(clientStream.IpEndPointPair.RemoteEndPoint, session, SessionErrorCode.UnsupportedClient, request.RequestId,
                "This client is outdated and not supported anymore! Please update your app.");

        // Report new session
        var clientIp = _sessionManager.TrackingOptions.TrackClientIpValue ? VhLogger.Format(ipEndPointPair.RemoteEndPoint.Address) : "*";

        // report in main log
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "New Session. SessionId: {SessionId}, Agent: {Agent}", VhLogger.FormatSessionId(session.SessionId), request.ClientInfo.UserAgent);

        // report in session log
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tTokenId: {TokenId}\tClientCount: {ClientCount,-3}\tClientId: {ClientId}\tClientIp: {ClientIp-15}\tVersion: {Version}\tOS: {OS}",
            VhLogger.FormatSessionId(session.SessionId), "New", VhLogger.FormatId(request.TokenId), session.SessionResponse.AccessUsage?.ActiveClientCount, VhLogger.FormatId(request.ClientInfo.ClientId), clientIp, request.ClientInfo.ClientVersion, UserAgentParser.GetOperatingSystem(request.ClientInfo.UserAgent));

        // report in track log
        if (_sessionManager.TrackingOptions.IsEnabled)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Track,
                "{Proto}; SessionId {SessionId}; TokenId {TokenId}; ClientIp {clientIp}".Replace("; ", "\t"),
                "NewS", VhLogger.FormatSessionId(session.SessionId), VhLogger.FormatId(request.TokenId), clientIp);
        }

        // reply hello session
        VhLogger.Instance.LogTrace(GeneralEventId.Session,
            $"Replying Hello response. SessionId: {VhLogger.FormatSessionId(sessionResponse.SessionId)}");

        // find udp port that match to the same tcp local IpAddress
        var udpPort = _udpChannelTransmitters
            .SingleOrDefault(x => x.LocalEndPoint.Address.Equals(ipEndPointPair.LocalEndPoint.Address))?.LocalEndPoint
            .Port;

        var helloResponse = new HelloResponse
        {
            ErrorCode = sessionResponse.ErrorCode,
            ErrorMessage = sessionResponse.ErrorMessage,
            AccessUsage = sessionResponse.AccessUsage,
            SuppressedBy = sessionResponse.SuppressedBy,
            RedirectHostEndPoint = sessionResponse.RedirectHostEndPoint,
            RedirectHostEndPoints = sessionResponse.RedirectHostEndPoints,
            SessionId = sessionResponse.SessionId,
            SessionKey = sessionResponse.SessionKey,
            ServerSecret = _sessionManager.ServerSecret,
            UdpPort = udpPort,
            GaMeasurementId = sessionResponse.GaMeasurementId,
            ServerVersion = _sessionManager.ServerVersion.ToString(3),
            ServerProtocolVersion = ServerProtocolVersion,
            SuppressedTo = sessionResponse.SuppressedTo,
            MaxDatagramChannelCount = session.Tunnel.MaxDatagramChannelCount,
            ClientPublicAddress = ipEndPointPair.RemoteEndPoint.Address,
            IncludeIpRanges = NetFilterIncludeIpRanges,
            PacketCaptureIncludeIpRanges = NetFilterPacketCaptureIncludeIpRanges,
            IsIpV6Supported = IsIpV6Supported,
            // client should wait more to get session exception replies
            RequestTimeout = _sessionManager.SessionOptions.TcpConnectTimeoutValue + TunnelDefaults.ClientRequestTimeoutDelta,
            // client should wait less to make sure server is not closing the connection
            TcpReuseTimeout = _sessionManager.SessionOptions.TcpReuseTimeoutValue - TunnelDefaults.ClientRequestTimeoutDelta,
            AccessKey = sessionResponse.AccessKey,
            DnsServers = DnsServers,
            AdRequirement = sessionResponse.AdRequirement,
            ServerLocation = sessionResponse.ServerLocation
        };
        await StreamUtil.WriteJsonAsync(clientStream.Stream, helloResponse, cancellationToken).VhConfigureAwait();
        await clientStream.DisposeAsync().VhConfigureAwait();
    }

    private async Task ProcessAdRewardRequest(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Reading the RewardAd request...");
        var request = await ReadRequest<AdRewardRequest>(clientStream, cancellationToken).VhConfigureAwait();
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessAdRewardRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private static async Task ProcessServerStatus(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Reading the ServerStatus request...");
        var request = await ReadRequest<ServerStatusRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // Before calling CloseSession. Session must be validated by GetSession
        await StreamUtil.WriteJsonAsync(clientStream.Stream,
            new ServerStatusResponse
            {
                ErrorCode = SessionErrorCode.Ok,
                Message = request.Message == "Hi, How are you?" ? "I am OK. How are you?" : "OK. Who are you?"

            }, cancellationToken)
            .VhConfigureAwait();

        await clientStream.DisposeAsync().VhConfigureAwait();
    }

    private async Task ProcessSessionStatus(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Reading the SessionStatus request...");
        var request = await ReadRequest<SessionStatusRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // finding session
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();

        // processing request
        await session.ProcessSessionStatusRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private async Task ProcessBye(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Reading the Bye request...");
        var request = await ReadRequest<ByeRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();

        // Before calling CloseSession. Session must be validated by GetSession
        await StreamUtil.WriteJsonAsync(clientStream.Stream, new SessionResponse { ErrorCode = SessionErrorCode.Ok }, cancellationToken).VhConfigureAwait();
        await clientStream.DisposeAsync(false).VhConfigureAwait();

        // must be last
        await _sessionManager.CloseSession(session.SessionId).VhConfigureAwait();
    }

    private async Task ProcessUdpPacketRequest(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Reading a UdpPacket request...");
        var request = await ReadRequest<UdpPacketRequest>(clientStream, cancellationToken).VhConfigureAwait();

        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessUdpPacketRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private async Task ProcessTcpDatagramChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel, "Reading the TcpDatagramChannelRequest...");
        var request = await ReadRequest<TcpDatagramChannelRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessTcpDatagramChannelRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private async Task ProcessStreamProxyChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.StreamProxyChannel, "Reading the StreamProxyChannelRequest...");
        var request = await ReadRequest<StreamProxyChannelRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // find session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessTcpProxyRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    public Task RunJob()
    {
        lock (_clientStreams)
            _clientStreams.RemoveWhere(x => x.Disposed);

        return Task.CompletedTask;
    }

    private async Task Stop()
    {
        VhLogger.Instance.LogTrace("Stopping ServerHost...");
        _cancellationTokenSource.Cancel();

        // UDPs
        VhLogger.Instance.LogTrace("Disposing UdpChannelTransmitters...");
        foreach (var udpChannelClient in _udpChannelTransmitters)
            udpChannelClient.Dispose();
        _udpChannelTransmitters.Clear();

        // TCPs
        VhLogger.Instance.LogTrace("Disposing TcpListeners...");
        foreach (var tcpListener in _tcpListeners)
            tcpListener.Stop();
        _tcpListeners.Clear();

        // dispose clientStreams
        VhLogger.Instance.LogTrace("Disposing ClientStreams...");
        Task[] disposeTasks;
        lock (_clientStreams)
            disposeTasks = _clientStreams.Select(x => x.DisposeAsync(false).AsTask()).ToArray();
        await Task.WhenAll(disposeTasks).VhConfigureAwait();

        // wait for finalizing all listener tasks
        VhLogger.Instance.LogTrace("Disposing current processing requests...");
        try
        {
            await VhUtil.RunTask(Task.WhenAll(_tcpListenerTasks), TimeSpan.FromSeconds(15)).VhConfigureAwait();
        }
        catch (Exception ex)
        {
            if (ex is TimeoutException)
                VhLogger.Instance.LogError(ex, "Error in stopping ServerHost.");
        }
        _tcpListenerTasks.Clear();
    }

    private readonly AsyncLock _disposeLock = new();
    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;
        await Stop().VhConfigureAwait();
    }

    // cache HostName for performance
    public class CertificateHostName(X509Certificate2 certificate)
    {
        public string HostName { get; } = certificate.GetNameInfo(X509NameType.DnsName, false) ?? throw new Exception("Could not get the HostName from the certificate.");
        public X509Certificate2 Certificate { get; } = certificate;
    }
}
