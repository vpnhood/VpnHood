using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Exceptions;
using VpnHood.Core.Server.Listeners;
using VpnHood.Core.Server.Utils;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Server;

public class ServerHost : IDisposable, IAsyncDisposable
{
    private readonly HashSet<IConnection> _connections = [];
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SessionManager _sessionManager;
    private readonly DownloadService _downloadService;
    private readonly TcpListenerHost _tcpListenerHost;
    private readonly QuicListenerHost _quicListenerHost;
    private readonly List<UdpChannelTransmitter> _udpChannelTransmitters = [];
    private readonly Job _cleanupConnectionsJob;
    private readonly AsyncLock _configureLock = new();
    private bool _disposed;

    public const int MaxProtocolVersion = 13;
    public const int MinProtocolVersion = 8;
    public int MinClientProtocolVersion { get; set; } = 8;
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? NetFilterVpnAdapterIncludeIpRanges { get; set; }
    public IpRange[]? NetFilterIncludeIpRanges { get; set; }
    public IPAddress[]? DnsServers { get; set; }
    public IReadOnlyList<CertificateHostName> Certificates { get; private set; } = [];
    public IReadOnlyList<IPEndPoint> TcpEndPoints => _tcpListenerHost.EndPoints;
    public IReadOnlyList<IPEndPoint> QuicEndPoints => _quicListenerHost.EndPoints;
    public bool Started => !_disposed && !_cancellationTokenSource.IsCancellationRequested;
    public string? DownloadPath => _downloadService.DownloadPath;

    public ServerHost(SessionManager sessionManager, string? downloadsPath)
    {
        _sessionManager = sessionManager;
        _downloadService = new DownloadService(downloadsPath);
        _cleanupConnectionsJob = new Job(CleanupConnections, TimeSpan.FromMinutes(5), nameof(ServerHost));
        UniqueIdFactory.DebugInitId = 5000;
        _tcpListenerHost = new TcpListenerHost(
            sessionManager: _sessionManager,
            cancellationToken: _cancellationTokenSource.Token,
            processNewConnection: ProcessNewConnection);
        _quicListenerHost = new QuicListenerHost(
            sessionManager: _sessionManager,
            cancellationToken: _cancellationTokenSource.Token,
            processNewConnection: ProcessNewConnection);
    }

    internal async Task Configure(ServerHostConfiguration configuration)
    {
        if (VhUtils.IsNullOrEmpty(configuration.Certificates))
            throw new ArgumentNullException(nameof(configuration.Certificates), "No certificate has been configured.");

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // wait for last configure to finish
        using var lockResult = await _configureLock.LockAsync(_cancellationTokenSource.Token).Vhc();

        // reconfigure
        DnsServers = configuration.DnsServers;
        Certificates = configuration.Certificates.Select(x => new CertificateHostName(x)).ToArray();

        await _tcpListenerHost.Configure(configuration.TcpEndPoints, Certificates).Vhc();
        await _quicListenerHost.Configure(configuration.QuicEndPoints, Certificates).Vhc();
        ConfigureUdpListeners(configuration.UdpEndPoints, configuration.UdpChannelBufferSize);
    }


    private void ConfigureUdpListeners(IPEndPoint[] udpEndPoints, TransferBufferSize? bufferSize)
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
            udpChannelTransmitter.BufferSize = bufferSize ?? TunnelDefaults.ServerUdpChannelBufferSize;
        }
    }

    private static async Task<int> ReadRequestVersion(IConnection connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];

        // read request version. We may wait here for reuse timeout
        var rest = await connection.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).Vhc();
        if (rest == 0) {
            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "Connection has been closed. ConnectionId: {ConnectionId}", connection.ConnectionId);
            connection.PreventReuse();
            await connection.DisposeAsync();
            return -1;
        }

        var version = buffer[0];
        return version;
    }

    private async Task<ServerConnection> CreateHttpConnection(IConnection connection, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogDebug(GeneralEventId.Request, "Waiting for Connection request...");
            var headers =
                await HttpUtils.ParseHeadersAsync(connection.Stream, cancellationToken).Vhc()
                ?? throw new Exception("Connection has been closed before receiving any request.");

            VhLogger.Instance.LogDebug(GeneralEventId.Request, "request: {request}", string.Join("\n", headers));

            Enum.TryParse<TunnelStreamType>(headers.GetValueOrDefault("X-BinaryStream", ""), out var streamType);
            bool.TryParse(headers.GetValueOrDefault("X-Buffered", "true"), out var useBuffer);
            int.TryParse(headers.GetValueOrDefault("X-ProtocolVersion", "0"), out var protocolVersion);
            var connectionId = headers.GetValueOrDefault("X-ConnectionId", connection.ConnectionId);
            var webSocketKey = headers.GetValueOrDefault("Sec-WebSocket-Key", "");
            var httpRequestLine = headers.GetValueOrDefault(HttpUtils.HttpRequestKey, "");
            var httpMethod = httpRequestLine.Split(" ").FirstOrDefault();
            var upgrade = headers.GetValueOrDefault("Upgrade", "");
            var clientIpByProxy = headers.GetValueOrDefault("X-Forwarded-For", "");
            var clientIp = ServerUtil.GetClientIpFromXForwarded(clientIpByProxy) ?? connection.RemoteEndPoint.Address;
            var isRevereProxy = !string.IsNullOrEmpty(clientIpByProxy);

            // Try to serve download file if requested
            if (await _downloadService.TryServeDownloadAsync(connection, httpRequestLine, cancellationToken).Vhc())
                throw new RequestHandledException(); // Should not reach here, but just in case

            if (upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                streamType = TunnelStreamType.WebSocket;

            // check version; Throw unauthorized to prevent fingerprinting
            if (protocolVersion is < MinProtocolVersion or > MaxProtocolVersion)
                throw new UnauthorizedAccessException();

            switch (streamType) {
                case TunnelStreamType.None:
                    if (httpMethod != "POST")
                        throw new Exception("Bad request");

                    return new ServerConnection(connection) {
                        ClientIp = clientIp,
                        RequireHttpResponse = true,
                        IsReverseProxy = isRevereProxy
                    };


                case TunnelStreamType.WebSocketNoHandshake: {
                        if (httpMethod != "POST")
                            throw new Exception("Bad request");

                        // create WebSocket stream without handshake
                        var websocketStream = new WebSocketStream(
                            connection.Stream, connection.ConnectionId, useBuffer: useBuffer, isServer: true);
                        var websocketConnection = new ConnectionDecorator(connection, websocketStream);
                        var reusableConnection = new ReusableConnection(websocketConnection, ReuseConnection);
                        return new ServerConnection(reusableConnection) {
                            ConnectionId = connectionId,
                            ClientIp = clientIp,
                            RequireHttpResponse = true, // same as simple use WebSocket stream
                            IsReverseProxy = isRevereProxy
                        };
                    }

                case TunnelStreamType.WebSocket: {
                        if (httpMethod != "GET")
                            throw new Exception("Bad request");

                        // reply HTTP response for websocket upgrade
                        var response = HttpResponseBuilder.WebSocketUpgrade(webSocketKey);
                        await connection.Stream.WriteAsync(response, cancellationToken).Vhc();

                        var websocketStream = new WebSocketStream(
                            connection.Stream, connection.ConnectionId, useBuffer: useBuffer, isServer: true);
                        var websocketConnection = new ConnectionDecorator(connection, websocketStream);
                        var reusableConnection = new ReusableConnection(websocketConnection, ReuseConnection);
                        return new ServerConnection(reusableConnection) {
                            ConnectionId = connectionId,
                            ClientIp = clientIp,
                            RequireHttpResponse = false, // Upgrade response has been already sent
                            IsReverseProxy = isRevereProxy
                        };
                    }

                case TunnelStreamType.Unknown:
                default:
                    throw new UnauthorizedAccessException(); //Unknown BinaryStreamType
            }
        }
        catch (Exception ex) when (connection.Connected) {
            //always return BadRequest 
            var response = ex is UnauthorizedAccessException
                ? HttpResponseBuilder.Unauthorized()
                : HttpResponseBuilder.Error(HttpStatusCode.BadRequest);
            await connection.Stream.WriteAsync(response, cancellationToken).Vhc();
            throw;
        }
    }

    private void ReuseConnection(IConnection connection)
    {
        // it handles the exception and dispose the stream
        _ = TryReuseConnectionAsync(connection);
    }

    private async Task TryReuseConnectionAsync(IConnection connection)
    {
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // add client stream to reuse list and take the ownership
            using var timeoutCts = new CancellationTokenSource(_sessionManager.SessionOptions.TcpReuseTimeoutValue);
            using var reusedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cancellationTokenSource.Token);

            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "ServerHost is pending a shared Connection for reuse. ConnectionId: {ConnectionId}",
                connection.ConnectionId);

            // wait for reuse timeout by reading the request version
            var requestVersion = await ReadRequestVersion(connection, reusedCts.Token).Vhc();
            if (requestVersion < 0)
                return; // client stream has been closed

            timeoutCts.CancelAfter(_sessionManager.SessionOptions.TcpReuseTimeoutValue); // reset timeout for new request
            await ProcessConnection(connection, requestVersion, clientIp: null, reusedCts.Token).Vhc();

            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "ServerHost has reused a shared Connection. ConnectionId: {ConnectionId}",
                connection.ConnectionId);
        }
        catch (Exception ex) {
            connection.PreventReuse();
            connection.Dispose();
            if (ex is ISelfLog loggable) loggable.Log();
            else
                VhLogger.Instance.LogDebug(GeneralEventId.Request, ex,
                    "ServerHost could not process this request on reused stream. ConnectionId: {ConnectionId}",
                    connection.ConnectionId);
        }
    }

    private async Task ProcessNewConnection(IConnection connection, CancellationToken cancellationToken)
    {
        var clientIp = connection.RemoteEndPoint.Address;
        try {
            var serverConnection = await CreateHttpConnection(connection, cancellationToken).Vhc();
            connection = serverConnection;
            clientIp = serverConnection.ClientIp;

            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "ServerHost has created a new connection. ConnectionId: {ConnectionId}, RemoteEp: {RemoteEp}, ClientIp: {ClientIp}",
                serverConnection.ConnectionId, VhLogger.Format(serverConnection.RemoteEndPoint),
                VhLogger.Format(serverConnection.ClientIp));

            var requestVersion = await ReadRequestVersion(serverConnection, cancellationToken).Vhc();
            if (requestVersion < 0)
                return;

            await ProcessConnection(connection, requestVersion, serverConnection.ClientIp, cancellationToken).Vhc();
        }
        catch (RequestHandledException) {
            // no need to do anything, just return and let the connection be closed by caller
            await connection.DisposeAsync();
        }
        catch (Exception ex) {
            if (connection is { Connected: true, RequireHttpResponse: true } &&
                !VhUtils.IsSocketClosedException(ex)) {
                VhLogger.Instance.LogDebug(GeneralEventId.Request,
                    "ServerHost is writing unauthorized response. ConnectionId: {ConnectionId}",
                    connection.ConnectionId);

                using var closeTimeoutCts = new CancellationTokenSource(_sessionManager.SessionOptions.TcpConnectTimeoutValue);
                using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(closeTimeoutCts.Token, cancellationToken);
                // ReSharper disable once AccessToDisposedClosure
                await VhUtils.TryInvokeAsync("Write unauthorized response.",
                    () => connection.Stream.WriteAsync(HttpResponseBuilder.Unauthorized(), closeCts.Token)).Vhc();
            }

            connection.PreventReuse();
            await connection.DisposeAsync();
            ex.Data["ConnectionId"] = connection.ConnectionId;
            ex.Data["ClientIp"] = VhLogger.Format(clientIp);
            throw;
        }
    }

    private async Task ProcessConnection(IConnection connection, int requestVersion, IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        using var scope = VhLogger.Instance.BeginScope($"ConnectionId: {connection.ConnectionId}");

        try {
            // Take ownership: stream is added here and removed in finally
            lock (_connections)
                _connections.Add(connection);

            await ProcessRequest(connection, requestVersion, clientIp: clientIp, cancellationToken).Vhc();
        }
        catch (SessionException ex) {
            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogDebug(GeneralEventId.Request, ex,
                    "Could not process this request. SessionErrorCode: {SessionErrorCode}, ConnectionId: {ConnectionId}",
                    ex.SessionResponse.ErrorCode, connection.ConnectionId);

            // reply the error to caller if it is SessionException
            // create a new CancellationTokenSource for close timeout
            using var closeTimeoutCts = new CancellationTokenSource(_sessionManager.SessionOptions.TcpConnectTimeoutValue);
            using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(closeTimeoutCts.Token, _cancellationTokenSource.Token);
            await connection.DisposeAsync(ex.SessionResponse, closeCts.Token).Vhc();
        }
        finally {
            lock (_connections)
                _connections.Remove(connection);
        }
    }

    private async Task ProcessRequest(IConnection connection, int requestVersion, IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        if (requestVersion != 1)
            throw new NotSupportedException($"The request version is not supported. Version: {requestVersion}");

        var buffer = new byte[1];

        // read request code
        var res = await connection.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).Vhc();
        if (res == 0)
            throw new Exception("Connection has been closed before reading the request code.");

        var requestCode = (RequestCode)buffer[0];
        switch (requestCode) {
            case RequestCode.Hello:
                if (clientIp is null)
                    throw new InvalidOperationException("Hello request should come by ClientIp.");
                await ProcessHello(connection, clientIp, cancellationToken).Vhc();
                break;

            case RequestCode.SessionStatus:
                await ProcessSessionStatus(connection, cancellationToken).Vhc();
                break;

            case RequestCode.TcpPacketChannel:
                await ProcessTcpPacketChannel(connection, cancellationToken).Vhc();
                break;

            case RequestCode.ProxyChannel:
                await ProcessStreamProxyChannel(connection, cancellationToken).Vhc();
                break;

            case RequestCode.UdpPacket:
                await ProcessUdpPacketRequest(connection, cancellationToken).Vhc();
                break;

            case RequestCode.RewardedAd:
                await ProcessRewardedAdRequest(connection, cancellationToken).Vhc();
                break;

            case RequestCode.ServerCheck:
                await ProcessServerCheck(connection, cancellationToken).Vhc();
                break;

            case RequestCode.Bye:
                await ProcessBye(connection, cancellationToken).Vhc();
                break;

            default:
                throw new NotSupportedException($"Unknown requestCode. requestCode: {requestCode}");
        }
    }

    private static async Task<T> ReadRequest<T>(IConnection connection, CancellationToken cancellationToken)
        where T : ClientRequest
    {
        // reading request
        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            "Processing a request. RequestType: {RequestType}.",
            VhLogger.FormatType<T>());

        var request = await StreamUtils.ReadObjectAsync<T>(connection.Stream, cancellationToken).Vhc();

        // check request id to server
        connection.ConnectionId = request.RequestId;

        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            "Request has been read. RequestType: {RequestType}. RequestId: {RequestId}",
            VhLogger.FormatType<T>(), request.RequestId);

        return request;
    }

    private static int? FindMatchingPort(IEnumerable<IPEndPoint> endPoints, IConnection connection, string connectionType)
    {
        var endPoint = endPoints.FirstOrDefault(x =>
            (x.Address.Equals(IPAddress.Any) && connection.LocalEndPoint.IsV4()) ||
            (x.Address.Equals(IPAddress.IPv6Any) && connection.LocalEndPoint.IsV6()) ||
            x.Address.Equals(connection.LocalEndPoint.Address));

        if (endPoint != null && connection is ServerConnection { IsReverseProxy: true }) {
            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "The connection is from reverse proxy, so {ConnectionType} port will not be returned in Hello response. ConnectionId: {ConnectionId}",
                connectionType, connection.ConnectionId);
            return null;
        }

        return endPoint?.Port;
    }

    private async Task ProcessHello(IConnection connection, IPAddress clientIp,
        CancellationToken cancellationToken)
    {
        //var ipEndPointPair = connection.IpEndPointPair;

        // reading request
        VhLogger.Instance.LogDebug(GeneralEventId.Request, "Processing hello request...");

        var request = await ReadRequest<HelloRequest>(connection, cancellationToken).Vhc();
        using var scope = CreateLogScope(connection, request);

        // find best version (for future)
        var protocolVersion = Math.Min(MaxProtocolVersion, request.ClientInfo.MaxProtocolVersion);

        // creating a session
        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            "Creating a session... TokenId: {TokenId}, ClientId: {ClientId}, ClientVersion: {ClientVersion}, UserAgent: {UserAgent}",
            VhLogger.FormatId(request.TokenId), VhLogger.FormatId(request.ClientInfo.ClientId),
            request.ClientInfo.ClientVersion, request.ClientInfo.UserAgent);

        var sessionResponseEx = await _sessionManager.CreateSession(
            request, connection.ToEndPointPair(), protocolVersion, clientIp, cancellationToken).Vhc();

        var session = _sessionManager.GetSessionById(sessionResponseEx.SessionId) ??
            throw new InvalidOperationException("Session is lost!");

        // check client version; unfortunately it must be after CreateSession to preserve server anonymity
        if (request.ClientInfo == null || protocolVersion < MinClientProtocolVersion)
            throw new ServerSessionException(connection.RemoteEndPoint, session,
                SessionErrorCode.UnsupportedClient, request.RequestId,
                "This client is outdated and not supported anymore! Please update your app.");

        // Report new session
        var clientIpText = _sessionManager.TrackingOptions.TrackClientIpValue
            ? VhLogger.Format(clientIp)
            : "*";

        // report in main log
        VhLogger.Instance.LogInformation(GeneralEventId.Request, "New Session. SessionId: {SessionId}, Agent: {Agent}",
            VhLogger.FormatSessionId(session.SessionId), request.ClientInfo.UserAgent);

        // report in session log
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tTokenId: {TokenId}\tClientCount: {ClientCount,-3}\tClientId: {ClientId}\t" +
            "ClientIp: {ClientIp-15}\tVersion: {Version}\tVirtualIp: {VirtualIp}\tOS: {OS}",
            VhLogger.FormatSessionId(session.SessionId), "New", VhLogger.FormatId(request.TokenId),
            session.SessionResponseEx.AccessUsage?.ActiveClientCount, VhLogger.FormatId(request.ClientInfo.ClientId),
            clientIpText, request.ClientInfo.ClientVersion,
            UserAgentParser.GetOperatingSystem(request.ClientInfo.UserAgent),
            session.VirtualIps.IpV4);

        // report in track log
        if (_sessionManager.TrackingOptions.IsEnabled) {
            VhLogger.Instance.LogInformation(GeneralEventId.Track,
                "{Proto}; SessionId {SessionId}; TokenId {TokenId}; ClientIp {clientIp}".Replace("; ", "\t"),
                "NewS", VhLogger.FormatSessionId(session.SessionId), VhLogger.FormatId(request.TokenId), clientIpText);
        }

        // reply hello session
        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            $"Replying Hello response. SessionId: {VhLogger.FormatSessionId(sessionResponseEx.SessionId)}");

        // find udp/quic port that matches the same local IP address family
        var udpPort = FindMatchingPort(_udpChannelTransmitters.Select(x => x.LocalEndPoint), connection, "UDP");
        var quicPort = FindMatchingPort(_quicListenerHost.EndPoints, connection, "QUIC");

        var helloResponse = new HelloResponse {
            ErrorCode = sessionResponseEx.ErrorCode,
            ErrorMessage = sessionResponseEx.ErrorMessage,
            AccessUsage = sessionResponseEx.AccessUsage,
            SuppressedBy = sessionResponseEx.SuppressedBy,
            SessionId = sessionResponseEx.SessionId,
            SessionKey = sessionResponseEx.SessionKey,
            ServerSecret = _sessionManager.ServerSecret,
            UdpPort = protocolVersion > 10 ? udpPort : null,
            QuicPort = quicPort,
            GaMeasurementId = sessionResponseEx.GaMeasurementId,
            ServerVersion = _sessionManager.ServerVersion.ToString(3),
            ProtocolVersion = sessionResponseEx.ProtocolVersion,
            SuppressedTo = sessionResponseEx.SuppressedTo,
            MaxPacketChannelCount = session.Tunnel.MaxPacketChannelCount,
            IncludeIpRanges = NetFilterIncludeIpRanges,
            VpnAdapterIncludeIpRanges = NetFilterVpnAdapterIncludeIpRanges,
            IsIpV6Supported = IsIpV6Supported,
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
            IsTcpPacketSupported = _sessionManager.IsVpnAdapterSupported && session.AllowTcpPacket,
            IsTcpProxySupported = session.AllowTcpProxy,
            ClientPublicAddress = clientIp,
            ClientCountry = sessionResponseEx.ClientCountry,
            VirtualIpNetworkV4 = new IpNetwork(session.VirtualIps.IpV4, _sessionManager.VirtualIpNetworkV4.PrefixLength),
            VirtualIpNetworkV6 = new IpNetwork(session.VirtualIps.IpV6, _sessionManager.VirtualIpNetworkV6.PrefixLength)
        };

        await connection.DisposeAsync(helloResponse, cancellationToken).Vhc();
    }

    private async Task ProcessRewardedAdRequest(IConnection connection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Request, "Reading the RewardedAd request...");
        var request = await ReadRequest<RewardedAdRequest>(connection, cancellationToken).Vhc();
        using var scope = CreateLogScope(connection, request);

        var session = await _sessionManager.GetSession(request, connection.ToEndPointPair(), cancellationToken).Vhc();
        await session.ProcessRewardedAdRequest(request, connection, cancellationToken).Vhc();
    }

    // todo: it must be removed
    private async Task ProcessSessionStatus(IConnection connection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Request, "Reading the SessionStatus request...");
        var request = await ReadRequest<SessionStatusRequest>(connection, cancellationToken).Vhc();
        using var scope = CreateLogScope(connection, request);

        // finding session
        var session = await _sessionManager.GetSession(request, connection.ToEndPointPair(), cancellationToken).Vhc();

        // processing request
        await session.ProcessSessionStatusRequest(request, connection, cancellationToken).Vhc();
    }

    private static async Task ProcessServerCheck(IConnection connection, CancellationToken cancellationToken)
    {
        // unauthorized reply is enough to prevent fingerprinting and let the client knows that the server is alive
        // no need to read the request, the header is enough to identify the request because bearer token exists in requests
        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            "Process ServerCheck and return unauthorized for the request...");

        // write unauthorized response
        await connection.Stream.WriteAsync(HttpResponseBuilder.Unauthorized(), cancellationToken).Vhc();
        connection.PreventReuse();
        await connection.DisposeAsync();
    }

    private async Task ProcessBye(IConnection connection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Request, "Reading the Bye request...");
        var request = await ReadRequest<ByeRequest>(connection, cancellationToken).Vhc();
        using var scope = CreateLogScope(connection, request);

        // finding session
        var session = await _sessionManager.GetSession(request, connection.ToEndPointPair(), cancellationToken).Vhc();

        // Dispose client stream
        if (connection is ReusableConnection reusableConnection)
            reusableConnection.PreventReuse();
        await connection.DisposeAsync(new SessionResponse { ErrorCode = SessionErrorCode.Ok }, cancellationToken);

        // must be last
        await _sessionManager.CloseSession(session.SessionId, cancellationToken);
    }

    private async Task ProcessUdpPacketRequest(IConnection connection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Request, "Reading a UdpPacket request...");
        var request = await ReadRequest<UdpPacketRequest>(connection, cancellationToken).Vhc();
        using var scope = CreateLogScope(connection, request);

        var session = await _sessionManager.GetSession(request, connection.ToEndPointPair(), cancellationToken).Vhc();
        await session.ProcessUdpPacketRequest(request, connection, cancellationToken).Vhc();
    }

    private async Task ProcessTcpPacketChannel(IConnection connection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Request, "Reading the TcpPacketChannelRequest...");
        var request = await ReadRequest<TcpPacketChannelRequest>(connection, cancellationToken).Vhc();
        using var scope = CreateLogScope(connection, request);

        // finding session
        var session = await _sessionManager.GetSession(request, connection.ToEndPointPair(), cancellationToken).Vhc();
        await session.ProcessTcpPacketChannelRequest(request, connection, cancellationToken).Vhc();
    }

    private async Task ProcessStreamProxyChannel(IConnection connection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Reading the StreamProxyChannelRequest...");
        var request = await ReadRequest<StreamProxyChannelRequest>(connection, cancellationToken).Vhc();
        using var scope = CreateLogScope(connection, request);

        // find session
        var session = await _sessionManager.GetSession(request, connection.ToEndPointPair(), cancellationToken).Vhc();
        await session.ProcessTcpProxyRequest(request, connection, cancellationToken).Vhc();
    }

    private ValueTask CleanupConnections(CancellationToken cancellationToken)
    {
        lock (_connections)
            _connections.RemoveWhere(x => !x.Connected);

        return default;
    }

    private static IDisposable? CreateLogScope(IConnection connection, ClientRequest request)
    {
        var state = new StringBuilder();
        if (request is RequestBase requestBase)
            state.Append($"SessionId: {VhLogger.FormatSessionId(requestBase.SessionId)}, ");
        state.Append($"StreamId: {connection.ConnectionId}, RequestId: {request.RequestId}");
        return VhLogger.Instance.BeginScope(state.ToString());
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(15));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // cancel all running tasks
        VhLogger.Instance.LogDebug("Stopping ServerHost...");
        await _cancellationTokenSource.TryCancelAsync();
        _cancellationTokenSource.Dispose();

        // Job
        _cleanupConnectionsJob.Dispose();

        // UDPs
        VhLogger.Instance.LogDebug("Disposing UdpChannelTransmitters...");
        foreach (var udpChannelClient in _udpChannelTransmitters)
            udpChannelClient.Dispose();
        _udpChannelTransmitters.Clear();

        // QUIC + TCPs in parallel
        VhLogger.Instance.LogDebug("Disposing QuicListeners and TcpListeners...");
        await Task.WhenAll(_quicListenerHost.StopAllAsync().AsTask(), _tcpListenerHost.StopAllAsync().AsTask()).Vhc();

        // dispose connections
        VhLogger.Instance.LogDebug("Disposing Connections...");
        lock (_connections)
            foreach (var connection in _connections) {
                connection.PreventReuse();
                connection.Dispose();
            }
    }
}

