using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Exceptions;
using VpnHood.Core.Server.Utils;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Server;

public class ServerHost : IDisposable, IAsyncDisposable
{
    private readonly HashSet<IClientStream> _clientStreams = [];
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SessionManager _sessionManager;
    private readonly List<TcpListener> _tcpListeners;
    private readonly List<UdpChannelTransmitter> _udpChannelTransmitters = [];
    private readonly List<Task> _tcpListenerTasks = [];
    private bool _disposed;
    private readonly Job _cleanupConnectionsJob;

    public const int MaxProtocolVersion = 8;
    public const int MinProtocolVersion = 4;
    public int MinClientProtocolVersion { get; set; } = 8;
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? NetFilterVpnAdapterIncludeIpRanges { get; set; }
    public IpRange[]? NetFilterIncludeIpRanges { get; set; }
    public IPAddress[]? DnsServers { get; set; }
    public CertificateHostName[] Certificates { get; private set; } = [];
    public IPEndPoint[] TcpEndPoints => _tcpListeners.Select(x => (IPEndPoint)x.LocalEndpoint).ToArray();
    public bool Started => !_disposed && !_cancellationTokenSource.IsCancellationRequested;

    public ServerHost(SessionManager sessionManager)
    {
        _tcpListeners = [];
        _sessionManager = sessionManager;
        _cleanupConnectionsJob = new Job(CleanupConnections, TimeSpan.FromMinutes(5), nameof(ServerHost));
    }

    internal async Task Configure(ServerHostConfiguration configuration)
    {
        if (VhUtils.IsNullOrEmpty(configuration.Certificates))
            throw new ArgumentNullException(nameof(configuration.Certificates), "No certificate has been configured.");

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // wait for last configure to finish
        using var lockResult = await _configureLock.LockAsync(_cancellationTokenSource.Token).VhConfigureAwait();

        // reconfigure
        DnsServers = configuration.DnsServers;
        Certificates = configuration.Certificates.Select(x => new CertificateHostName(x)).ToArray();

        ConfigureTcpListeners(configuration.TcpEndPoints);
        ConfigureUdpListeners(configuration.UdpEndPoints, configuration.UdpSendBufferSize, configuration.UdpReceiveBufferSize);
        _tcpListenerTasks.RemoveAll(x => x.IsCompleted);
    }

    private readonly AsyncLock _configureLock = new();

    private void ConfigureUdpListeners(IPEndPoint[] udpEndPoints, int? sendBufferSize, int? receiveBufferSize)
    {
        // UDP port zero must be specified in preparation
        if (udpEndPoints.Any(x => x.Port == 0))
            throw new InvalidOperationException("UDP port has not been specified.");

        // stop listeners that are not in the list
        foreach (var udpChannelTransmitter in _udpChannelTransmitters
                     .Where(x => !udpEndPoints.Contains(x.LocalEndPoint)).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on UdpEndPoint: {UdpEndPoint}",
                VhLogger.Format(udpChannelTransmitter.LocalEndPoint));
            udpChannelTransmitter.Dispose();
            _udpChannelTransmitters.Remove(udpChannelTransmitter);
        }

        // start new listeners
        foreach (var udpEndPoint in udpEndPoints) {
            try {
                // ignore already listening
                if (_udpChannelTransmitters.Any(x => x.LocalEndPoint.Equals(udpEndPoint)))
                    continue;

                VhLogger.Instance.LogInformation("Start listening on UdpEndPoint: {UdpEndPoint}",
                        VhLogger.Format(udpEndPoint));

                var udpChannelTransmitter = ServerUdpChannelTransmitter.Create(udpEndPoint, _sessionManager);
                _udpChannelTransmitters.Add(udpChannelTransmitter);
            }
            catch (Exception ex) {
                ex.Data.Add("UdpEndPoint", udpEndPoint);
                throw;
            }
        }

        // reconfigure all transmitters
        foreach (var udpChannelTransmitter in _udpChannelTransmitters) {
            udpChannelTransmitter.SendBufferSize = sendBufferSize;
            udpChannelTransmitter.ReceiveBufferSize = receiveBufferSize;
        }
    }

    private void ConfigureTcpListeners(IPEndPoint[] tcpEndPoints)
    {
        if (tcpEndPoints.Length == 0)
            throw new ArgumentNullException(nameof(tcpEndPoints), "No TcpEndPoint has been configured.");

        // start TCPs
        // stop listeners that are not in the new list
        foreach (var tcpListener in _tcpListeners
                     .Where(x => !tcpEndPoints.Contains((IPEndPoint)x.LocalEndpoint)).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on TcpEndPoint: {TcpEndPoint}",
                VhLogger.Format(tcpListener.LocalEndpoint));
            tcpListener.Stop();
            _tcpListeners.Remove(tcpListener);
        }

        foreach (var tcpEndPoint in tcpEndPoints) {
            // check already listening
            if (_tcpListeners.Any(x => x.LocalEndpoint.Equals(tcpEndPoint)))
                continue;

            try {
                VhLogger.Instance.LogInformation("Start listening on TcpEndPoint: {TcpEndPoint}",
                    VhLogger.Format(tcpEndPoint));
                var tcpListener = new TcpListener(tcpEndPoint);
                tcpListener.Start();
                _tcpListeners.Add(tcpListener);
                _tcpListenerTasks.Add(ListenTask(tcpListener));
            }
            catch (Exception ex) {
                VhLogger.Instance.LogInformation("Error listening on TcpEndPoint: {TcpEndPoint}",
                    VhLogger.Format(tcpEndPoint));
                ex.Data.Add("TcpEndPoint", tcpEndPoint);
                throw;
            }
        }
    }

    private async Task ListenTask(TcpListener tcpListener)
    {
        var localEp = (IPEndPoint)tcpListener.LocalEndpoint;
        var errorCounter = 0;
        const int maxErrorCount = 200;

        // Listening for new connection
        while (Started) {
            try {
                var tcpClient = await tcpListener.AcceptTcpClientAsync().VhConfigureAwait();
                if (!Started)
                    throw new ObjectDisposedException("ServerHost has been stopped.");

                VhUtils.ConfigTcpClient(tcpClient,
                    _sessionManager.SessionOptions.TcpKernelSendBufferSize,
                    _sessionManager.SessionOptions.TcpKernelReceiveBufferSize);

                // config tcpClient
                _ = ProcessTcpClient(tcpClient, _cancellationTokenSource.Token);
                errorCounter = 0;
            }
            catch (Exception) when (!tcpListener.Server.IsBound) {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted) {
                errorCounter++;
            }
            catch (ObjectDisposedException) {
                errorCounter++;
            }
            catch (Exception ex) {
                if (Started)
                    break; // no log

                errorCounter++;
                if (errorCounter > maxErrorCount) {
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

        // stop listener
        tcpListener.Stop();
        VhLogger.Instance.LogInformation("TCP Listener has been stopped. LocalEp: {LocalEp}", VhLogger.Format(localEp));
    }

    private async Task<SslStream> AuthenticateAsServerAsync(Stream stream, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogDebug(GeneralEventId.Tcp, "TLS Authenticating...");
            var sslStream = new SslStream(stream, true);
            await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions {
                        ClientCertificateRequired = false,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        ServerCertificateSelectionCallback = ServerCertificateSelectionCallback
                    },
                    cancellationToken)
                .VhConfigureAwait();
            return sslStream;
        }
        catch (Exception ex) {
            throw new TlsAuthenticateException("TLS Authentication error.", ex);
        }
    }

    private X509Certificate ServerCertificateSelectionCallback(object sender, string hostname)
    {
        var certificate =
            Certificates.SingleOrDefault(x => x.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
            ?? Certificates.First();

        return certificate.Certificate;
    }

    private bool CheckApiKeyAuthorization(string authorization)
    {
        var parts = authorization.Split(' ');
        return
            parts.Length >= 2 &&
            parts[0].Equals("ApiKey", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals(_sessionManager.ApiKey, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Tcp, "Waiting for request...");
        var streamId = UniqueIdFactory.Create() + ":server:tunnel:incoming";

        // Version 2 is HTTP and starts with POST
        try {
            var headers =
                await HttpUtil.ParseHeadersAsync(sslStream, cancellationToken).VhConfigureAwait()
                ?? throw new Exception("Connection has been closed before receiving any request.");

            Enum.TryParse<TunnelStreamType>(headers.GetValueOrDefault("X-BinaryStream", ""), out var binaryStreamType);
            bool.TryParse(headers.GetValueOrDefault("X-Buffered", "true"), out var useBuffer);
            int.TryParse(headers.GetValueOrDefault("X-ProtocolVersion", "5"), out var protocolVersion);
            var authorization = headers.GetValueOrDefault("Authorization", string.Empty);

            // check version; Throw unauthorized to prevent fingerprinting
            if (protocolVersion is < MinProtocolVersion or > MaxProtocolVersion)
                throw new UnauthorizedAccessException();

            // read api key
            if (protocolVersion <= 5) {
                if (!CheckApiKeyAuthorization(authorization)) {
                    // process hello without api key
                    if (authorization != "ApiKey")
                        throw new UnauthorizedAccessException();

                    await sslStream.WriteAsync(HttpResponseBuilder.Unauthorized(), cancellationToken)
                        .VhConfigureAwait();
                    return new TcpClientStream(tcpClient, sslStream, streamId);
                }

                await sslStream.WriteAsync(HttpResponseBuilder.Ok(), cancellationToken).VhConfigureAwait();
            }

            switch (binaryStreamType) {
                case TunnelStreamType.Standard when protocolVersion <= 5:
                    return new TcpClientStream(tcpClient,
                        new BinaryStreamStandard(tcpClient.GetStream(), streamId, useBuffer),
                        streamId, ReuseClientStream) {
                        RequireHttpResponse = false
                    };

                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                case TunnelStreamType.Standard when protocolVersion >= 6:
                    return new TcpClientStream(tcpClient,
                        new BinaryStreamStandard(sslStream, streamId, useBuffer),
                        streamId, ReuseClientStream) {
                        RequireHttpResponse = true
                    };

                case TunnelStreamType.None:
                    return new TcpClientStream(tcpClient, sslStream, streamId) {
                        RequireHttpResponse = protocolVersion >= 6
                    };

                case TunnelStreamType.Unknown:
                default:
                    throw new UnauthorizedAccessException(); //Unknown BinaryStreamType
            }
        }
        catch (Exception ex) {
            //always return BadRequest 
            if (!VhUtils.IsTcpClientHealthy(tcpClient)) throw;
            var response = ex is UnauthorizedAccessException
                ? HttpResponseBuilder.Unauthorized()
                : HttpResponseBuilder.BadRequest();
            await sslStream.WriteAsync(response, cancellationToken).VhConfigureAwait();
            throw;
        }
    }

    private async Task ProcessTcpClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        IClientStream? clientStream = null;
        SslStream? sslStream = null;
        try {
            // add timeout to cancellationToken
            using var timeoutCt = new CancellationTokenSource(_sessionManager.SessionOptions.TcpReuseTimeoutValue);
            using var localCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, cancellationToken);

            // establish SSL
            sslStream = await AuthenticateAsServerAsync(tcpClient.GetStream(), localCts.Token)
                .VhConfigureAwait();

            // create client stream
            clientStream = await CreateClientStream(tcpClient, sslStream, localCts.Token).VhConfigureAwait();
            await ProcessClientStream(clientStream, localCts.Token).VhConfigureAwait();
        }
        catch (TlsAuthenticateException ex) {
            VhLogger.Instance.LogInformation(GeneralEventId.Tls, ex, "Error in Client TLS authentication.");
            clientStream?.DisposeWithoutReuse();
            if (sslStream != null)
                await sslStream.SafeDisposeAsync();
            tcpClient.Dispose();
        }
        catch (Exception ex) {
            if (ex is ISelfLog loggable) loggable.Log();
            else
                VhLogger.LogError(GeneralEventId.Request, ex,
                    "ServerHost could not process this request. ClientStreamId: {ClientStreamId}",
                    clientStream?.ClientStreamId);

            clientStream?.DisposeWithoutReuse();
            if (sslStream != null)
                await sslStream.SafeDisposeAsync();
            tcpClient.Dispose();
        }
    }

    private void ReuseClientStream(IClientStream clientStream)
    {
        // it handles the exception and dispose the stream
        _ = ReuseClientStreamAsync(clientStream);
    }

    private async Task ReuseClientStreamAsync(IClientStream clientStream)
    {
        // process incoming client
        try {
            if (_disposed)
                throw new ObjectDisposedException("ServerHost has been stopped.");

            // add client stream to reuse list and take the ownership
            using var timeoutCt = new CancellationTokenSource(_sessionManager.SessionOptions.TcpReuseTimeoutValue);
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, _cancellationTokenSource.Token);
            var cancellationToken = cancellationTokenSource.Token;

            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "ServerHost.ReuseClientStream: A shared ClientStream is pending for reuse. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);

            await ProcessClientStream(clientStream, cancellationToken).VhConfigureAwait();

            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "ServerHost.ReuseClientStream: A shared ClientStream has been reused. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.TcpLife, ex,
                "ServerHost.ReuseClientStream: Could not reuse a ClientStream. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);

            clientStream.DisposeWithoutReuse();
        }
    }

    private async Task ProcessClientStream(IClientStream clientStream, CancellationToken cancellationToken)
    {
        using var scope = VhLogger.Instance.BeginScope($"RemoteEp: {VhLogger.Format(clientStream.IpEndPointPair.RemoteEndPoint)}");

        try {
            // Take ownership: stream is added here and removed in finally
            lock (_clientStreams)
                _clientStreams.Add(clientStream);

            await ProcessRequest(clientStream, cancellationToken).VhConfigureAwait();
        }
        catch (SessionException ex) {
            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(
                    ex.SessionResponse.ErrorCode == SessionErrorCode.GeneralError
                        ? GeneralEventId.Tcp
                        : GeneralEventId.Session, ex,
                    "Could not process the request. SessionErrorCode: {SessionErrorCode}",
                    ex.SessionResponse.ErrorCode);

            // reply the error to caller if it is SessionException
            // Should not reply anything when user is unknown. ServerUnauthorizedException will be thrown when user is unauthenticated
            await clientStream.DisposeAsync(ex.SessionResponse, cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex) when (VhLogger.IsSocketCloseException(ex)) {
            VhLogger.LogError(GeneralEventId.Tcp, ex,
                "Connection has been closed. ClientStreamId: {ClientStreamId}.",
                clientStream.ClientStreamId);

            clientStream.DisposeWithoutReuse();
        }
        catch (Exception ex) {
            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(GeneralEventId.Tcp, ex,
                    "Could not process the request and return 401. ClientStreamId: {ClientStreamId}",
                    clientStream.ClientStreamId);

            // return 401 for ANY non SessionException to keep server's anonymity
            await clientStream.Stream.WriteAsync(HttpResponseBuilder.Unauthorized(), cancellationToken).VhConfigureAwait();
            clientStream.DisposeWithoutReuse();
        }
        finally {
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
        switch (requestCode) {
            case RequestCode.Hello:
                await ProcessHello(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.SessionStatus:
                await ProcessSessionStatus(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.TcpPacketChannel:
                await ProcessTcpPacketChannel(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.ProxyChannel:
                await ProcessStreamProxyChannel(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.UdpPacket:
                await ProcessUdpPacketRequest(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.RewardedAd:
                await ProcessRewardedAdRequest(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.ServerCheck:
                await ProcessServerCheck(clientStream, cancellationToken).VhConfigureAwait();
                break;

            case RequestCode.Bye:
                await ProcessBye(clientStream, cancellationToken).VhConfigureAwait();
                break;

            default:
                throw new NotSupportedException($"Unknown requestCode. requestCode: {requestCode}");
        }
    }

    private static async Task<T> ReadRequest<T>(IClientStream clientStream, CancellationToken cancellationToken)
        where T : ClientRequest
    {
        // reading request
        VhLogger.Instance.LogDebug(GeneralEventId.Session,
            "Processing a request. RequestType: {RequestType}.",
            VhLogger.FormatType<T>());

        var request = await StreamUtils.ReadObjectAsync<T>(clientStream.Stream, cancellationToken).VhConfigureAwait();

        // check request id to server
        request.RequestId = request.RequestId.Replace(":client", ":server");

        // change and log request
        // change ClientStreamId if it was incoming
        if (clientStream.ClientStreamId.Contains(":incoming"))
            clientStream.ClientStreamId = request.RequestId + ":tunnel";

        VhLogger.Instance.LogDebug(GeneralEventId.Session,
            "Request has been read. RequestType: {RequestType}. RequestId: {RequestId}",
            VhLogger.FormatType<T>(), request.RequestId);

        return request;
    }

    private async Task ProcessHello(IClientStream clientStream, CancellationToken cancellationToken)
    {
        var ipEndPointPair = clientStream.IpEndPointPair;

        // reading request
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Processing hello request... ClientIp: {ClientIp}",
            VhLogger.Format(ipEndPointPair.RemoteEndPoint.Address));

        var request = await ReadRequest<HelloRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // find best version (for future)
        var protocolVersion = Math.Min(MaxProtocolVersion, request.ClientInfo.MaxProtocolVersion);

        // creating a session
        VhLogger.Instance.LogDebug(GeneralEventId.Session,
            "Creating a session... TokenId: {TokenId}, ClientId: {ClientId}, ClientVersion: {ClientVersion}, UserAgent: {UserAgent}",
            VhLogger.FormatId(request.TokenId), VhLogger.FormatId(request.ClientInfo.ClientId),
            request.ClientInfo.ClientVersion, request.ClientInfo.UserAgent);
        var sessionResponseEx = await _sessionManager.CreateSession(request, ipEndPointPair, protocolVersion).VhConfigureAwait();
        var session = _sessionManager.GetSessionById(sessionResponseEx.SessionId) ??
                      throw new InvalidOperationException("Session is lost!");

        // check client version; unfortunately it must be after CreateSession to preserve server anonymity
        if (request.ClientInfo == null || protocolVersion < MinClientProtocolVersion)
            throw new ServerSessionException(clientStream.IpEndPointPair.RemoteEndPoint, session,
                SessionErrorCode.UnsupportedClient, request.RequestId,
                "This client is outdated and not supported anymore! Please update your app.");

        // Report new session
        var clientIp = _sessionManager.TrackingOptions.TrackClientIpValue
            ? VhLogger.Format(ipEndPointPair.RemoteEndPoint.Address)
            : "*";

        // report in main log
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "New Session. SessionId: {SessionId}, Agent: {Agent}",
            VhLogger.FormatSessionId(session.SessionId), request.ClientInfo.UserAgent);

        // report in session log
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tTokenId: {TokenId}\tClientCount: {ClientCount,-3}\tClientId: {ClientId}\tClientIp: {ClientIp-15}\tVersion: {Version}\tOS: {OS}",
            VhLogger.FormatSessionId(session.SessionId), "New", VhLogger.FormatId(request.TokenId),
            session.SessionResponseEx.AccessUsage?.ActiveClientCount, VhLogger.FormatId(request.ClientInfo.ClientId),
            clientIp, request.ClientInfo.ClientVersion,
            UserAgentParser.GetOperatingSystem(request.ClientInfo.UserAgent));

        // report in track log
        if (_sessionManager.TrackingOptions.IsEnabled) {
            VhLogger.Instance.LogInformation(GeneralEventId.Track,
                "{Proto}; SessionId {SessionId}; TokenId {TokenId}; ClientIp {clientIp}".Replace("; ", "\t"),
                "NewS", VhLogger.FormatSessionId(session.SessionId), VhLogger.FormatId(request.TokenId), clientIp);
        }

        // reply hello session
        VhLogger.Instance.LogDebug(GeneralEventId.Session,
            $"Replying Hello response. SessionId: {VhLogger.FormatSessionId(sessionResponseEx.SessionId)}");

        // find udp port that match to the same tcp local IpAddress
        var udpPort = _udpChannelTransmitters
            .SingleOrDefault(x =>
                (x.LocalEndPoint.Address.Equals(IPAddress.Any) && ipEndPointPair.LocalEndPoint.IsV4()) ||
                (x.LocalEndPoint.Address.Equals(IPAddress.IPv6Any) && ipEndPointPair.LocalEndPoint.IsV6()) ||
                x.LocalEndPoint.Address.Equals(ipEndPointPair.LocalEndPoint.Address))?
            .LocalEndPoint.Port;

        var helloResponse = new HelloResponse {
            ErrorCode = sessionResponseEx.ErrorCode,
            ErrorMessage = sessionResponseEx.ErrorMessage,
            AccessUsage = sessionResponseEx.AccessUsage,
            SuppressedBy = sessionResponseEx.SuppressedBy,
            RedirectHostEndPoint = sessionResponseEx.RedirectHostEndPoint,
            RedirectHostEndPoints = sessionResponseEx.RedirectHostEndPoints,
            SessionId = sessionResponseEx.SessionId,
            SessionKey = sessionResponseEx.SessionKey,
            ServerSecret = _sessionManager.ServerSecret,
            UdpPort = udpPort,
            GaMeasurementId = sessionResponseEx.GaMeasurementId,
            ServerVersion = _sessionManager.ServerVersion.ToString(3),
#pragma warning disable CS0618 // Type or member is obsolete
            ServerProtocolVersion = protocolVersion,
            MaxProtocolVersion = 7,
            MinProtocolVersion = MinProtocolVersion,
#pragma warning restore CS0618 // Type or member is obsolete
            ProtocolVersion = sessionResponseEx.ProtocolVersion,
            SuppressedTo = sessionResponseEx.SuppressedTo,
            MaxPacketChannelCount = session.Tunnel.MaxPacketChannelCount,
            IncludeIpRanges = NetFilterIncludeIpRanges,
            VpnAdapterIncludeIpRanges = NetFilterVpnAdapterIncludeIpRanges,
            IsIpV6Supported = IsIpV6Supported,
            IsReviewRequested = sessionResponseEx.IsReviewRequested,
            // client should wait more to get session exception replies
            RequestTimeout = _sessionManager.SessionOptions.TcpConnectTimeoutValue +
                             TunnelDefaults.ClientRequestTimeoutDelta,
            // client should wait less to make sure server is not closing the connection
            TcpReuseTimeout = _sessionManager.SessionOptions.TcpReuseTimeoutValue -
                              TunnelDefaults.ClientRequestTimeoutDelta,
            AccessKey = sessionResponseEx.AccessKey,
            DnsServers = DnsServers,
            AdRequirement = sessionResponseEx.AdRequirement,
            ServerLocation = sessionResponseEx.ServerLocation,
            ServerTags = sessionResponseEx.ServerTags,
            AccessInfo = sessionResponseEx.AccessInfo,
            IsTunProviderSupported = _sessionManager.IsVpnAdapterSupported,
            ClientPublicAddress = ipEndPointPair.RemoteEndPoint.Address,
            ClientCountry = sessionResponseEx.ClientCountry,
            VirtualIpNetworkV4 = new IpNetwork(session.VirtualIps.IpV4, _sessionManager.VirtualIpNetworkV4.PrefixLength),
            VirtualIpNetworkV6 = new IpNetwork(session.VirtualIps.IpV6, _sessionManager.VirtualIpNetworkV6.PrefixLength)
        };

        await clientStream.DisposeAsync(helloResponse, cancellationToken).VhConfigureAwait();
    }

    private async Task ProcessRewardedAdRequest(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading the RewardedAd request...");
        var request = await ReadRequest<RewardedAdRequest>(clientStream, cancellationToken).VhConfigureAwait();
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessRewardedAdRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    // todo: it must be removed
    private async Task ProcessSessionStatus(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading the SessionStatus request...");
        var request = await ReadRequest<SessionStatusRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // finding session
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();

        // processing request
        await session.ProcessSessionStatusRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private static async Task ProcessServerCheck(IClientStream clientStream, CancellationToken cancellationToken)
    {
        // unauthorized reply is enough to prevent fingerprinting and let the client knows that the server is alive
        // no need to read the request, the header is enough to identify the request because bearer token exists in requests
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Process ServerCheck and return unauthorized for the request...");

        // write unauthorized response
        await clientStream.Stream.WriteAsync(HttpResponseBuilder.Unauthorized(), cancellationToken).VhConfigureAwait();
        clientStream.DisposeWithoutReuse();
    }


    private async Task ProcessBye(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading the Bye request...");
        var request = await ReadRequest<ByeRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();

        // Dispose client stream
        clientStream.PreventReuse();
        await clientStream.DisposeAsync(new SessionResponse { ErrorCode = SessionErrorCode.Ok }, cancellationToken);

        // must be last
        await _sessionManager.CloseSession(session.SessionId);
    }

    private async Task ProcessUdpPacketRequest(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading a UdpPacket request...");
        var request = await ReadRequest<UdpPacketRequest>(clientStream, cancellationToken).VhConfigureAwait();

        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessUdpPacketRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private async Task ProcessTcpPacketChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Reading the TcpPacketChannelRequest...");
        var request = await ReadRequest<TcpPacketChannelRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessTcpPacketChannelRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private async Task ProcessStreamProxyChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.ProxyChannel, "Reading the StreamProxyChannelRequest...");
        var request = await ReadRequest<StreamProxyChannelRequest>(clientStream, cancellationToken).VhConfigureAwait();

        // find session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).VhConfigureAwait();
        await session.ProcessTcpProxyRequest(request, clientStream, cancellationToken).VhConfigureAwait();
    }

    private ValueTask CleanupConnections(CancellationToken cancellationToken)
    {
        lock (_clientStreams)
            _clientStreams.RemoveWhere(x => !x.Connected);

        return default;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // cancel all running tasks
        VhLogger.Instance.LogDebug("Stopping ServerHost...");
        _cancellationTokenSource.Cancel();

        // Job
        _cleanupConnectionsJob.Dispose();

        // UDPs
        VhLogger.Instance.LogDebug("Disposing UdpChannelTransmitters...");
        foreach (var udpChannelClient in _udpChannelTransmitters)
            udpChannelClient.Dispose();
        _udpChannelTransmitters.Clear();

        // TCPs
        VhLogger.Instance.LogDebug("Disposing TcpListeners...");
        foreach (var tcpListener in _tcpListeners)
            tcpListener.Stop();
        _tcpListeners.Clear();

        // dispose clientStreams
        VhLogger.Instance.LogDebug("Disposing ClientStreams...");
        lock (_clientStreams)
            foreach (var clientStream in _clientStreams)
                clientStream.DisposeWithoutReuse();

        // clear list of listeners
        _tcpListenerTasks.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        var tcpListenerTasks = _tcpListenerTasks.ToArray();
        Dispose();

        // wait for finalizing all listener tasks
        VhLogger.Instance.LogDebug("Disposing current processing requests...");
        try {
            await VhUtils.RunTask(Task.WhenAll(tcpListenerTasks), TimeSpan.FromSeconds(15)).VhConfigureAwait();
        }
        catch (Exception ex) {
            if (ex is TimeoutException)
                VhLogger.Instance.LogError(ex, "Error in stopping ServerHost.");
        }
    }

    // cache HostName for performance
    public class CertificateHostName(X509Certificate2 certificate)
    {
        public string HostName { get; } = certificate.GetNameInfo(X509NameType.DnsName, false) ??
                                          throw new Exception("Could not get the HostName from the certificate.");

        public X509Certificate2 Certificate { get; } = certificate;
    }
}