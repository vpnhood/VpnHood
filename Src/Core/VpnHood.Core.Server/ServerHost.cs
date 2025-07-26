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

    public const int MaxProtocolVersion = 9;
    public const int MinProtocolVersion = 6;
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
        using var lockResult = await _configureLock.LockAsync(_cancellationTokenSource.Token).Vhc();

        // reconfigure
        DnsServers = configuration.DnsServers;
        Certificates = configuration.Certificates.Select(x => new CertificateHostName(x)).ToArray();

        ConfigureTcpListeners(configuration.TcpEndPoints);
        ConfigureUdpListeners(configuration.UdpEndPoints, configuration.UdpChannelBufferSize);
        _tcpListenerTasks.RemoveAll(x => x.IsCompleted);
    }

    private readonly AsyncLock _configureLock = new();

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
                var tcpClient = await tcpListener.AcceptTcpClientAsync().Vhc();
                if (!Started)
                    throw new ObjectDisposedException("ServerHost has been stopped.");

                VhUtils.ConfigTcpClient(tcpClient,
                    _sessionManager.SessionOptions.TcpKernelSendBufferSize,
                    _sessionManager.SessionOptions.TcpKernelReceiveBufferSize);

                // config tcpClient
                _ = TryProcessTcpClient(tcpClient, _cancellationTokenSource.Token);
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

    private X509Certificate ServerCertificateSelectionCallback(object sender, string? hostname)
    {
        var certificate =
            Certificates.SingleOrDefault(x => x.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
            ?? Certificates.First();

        return certificate.Certificate;
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream,
        CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogDebug(GeneralEventId.Tcp, "Waiting for ClientStream request...");
            var streamId = UniqueIdFactory.Create() + ":server:tunnel:incoming";

            var headers =
                await HttpUtil.ParseHeadersAsync(sslStream, cancellationToken).Vhc()
                ?? throw new Exception("Connection has been closed before receiving any request.");

            Enum.TryParse<TunnelStreamType>(headers.GetValueOrDefault("X-BinaryStream", ""), out var streamType);
            bool.TryParse(headers.GetValueOrDefault("X-Buffered", "true"), out var useBuffer);
            int.TryParse(headers.GetValueOrDefault("X-ProtocolVersion", "0"), out var protocolVersion);
            var webSocketKey = headers.GetValueOrDefault("Sec-WebSocket-Key", "");
            var httpMethod = headers.GetValueOrDefault(HttpUtil.HttpRequestKey, "").Split(" ").FirstOrDefault();
            var upgrade = headers.GetValueOrDefault("Upgrade", "");
            if (upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                streamType = TunnelStreamType.WebSocket;

            // check version; Throw unauthorized to prevent fingerprinting
            if (protocolVersion is < MinProtocolVersion or > MaxProtocolVersion)
                throw new UnauthorizedAccessException();

            switch (streamType) {
                case TunnelStreamType.None:
                    if (httpMethod != "POST")
                        throw new Exception("Bad request");

                    return new TcpClientStream(tcpClient, sslStream, streamId) {
                        RequireHttpResponse = true
                    };

                // obsoleted
#pragma warning disable CS0618 // Type or member is obsolete version >= 725
                case TunnelStreamType.Standard:
                    if (httpMethod != "POST")
                        throw new Exception("Bad request");

                    return new TcpClientStream(tcpClient,
                        new BinaryStreamStandard(sslStream, streamId, useBuffer),
                        streamId, ReuseClientStream) {
                        RequireHttpResponse = true
                    };
#pragma warning restore CS0618 // Type or member is obsolete

                case TunnelStreamType.WebSocketNoHandshake:
                    if (httpMethod != "POST")
                        throw new Exception("Bad request");

                    // create WebSocket stream without handshake
                    return new TcpClientStream(tcpClient,
                        new WebSocketStream(sslStream, streamId, useBuffer, isServer: true),
                        streamId, ReuseClientStream) {
                        RequireHttpResponse = false // Upgrade response has been already sent
                    };

                case TunnelStreamType.WebSocket:
                    if (httpMethod != "GET")
                        throw new Exception("Bad request");

                    // reply HTTP response
                    var response = HttpResponseBuilder.WebSocketUpgrade(webSocketKey);
                    await sslStream.WriteAsync(response, cancellationToken).Vhc();

                    // create WebSocket stream
                    return new TcpClientStream(tcpClient,
                        new WebSocketStream(sslStream, streamId, useBuffer, isServer: true),
                        streamId, ReuseClientStream) {
                        RequireHttpResponse = false // Upgrade response has been already sent
                    };

                case TunnelStreamType.Unknown:
                default:
                    throw new UnauthorizedAccessException(); //Unknown BinaryStreamType
            }
        }
        catch (Exception ex) when (VhUtils.IsTcpClientHealthy(tcpClient)) {
            //always return BadRequest 
            var response = ex is UnauthorizedAccessException
                ? HttpResponseBuilder.Unauthorized()
                : HttpResponseBuilder.BadRequest();
            await sslStream.WriteAsync(response, cancellationToken).Vhc();
            throw;
        }
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Tcp, "TLS Authenticating...");
        var sslStream = new SslStream(tcpClient.GetStream(), true);

        try {
            // establish SSL
            await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions {
                        ClientCertificateRequired = false,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        ServerCertificateSelectionCallback = ServerCertificateSelectionCallback
                    },
                    cancellationToken).Vhc();

            // create client stream
            var clientStream = await CreateClientStream(tcpClient, sslStream, cancellationToken).Vhc();
            return clientStream;
        }
        catch (Exception) {
            await sslStream.SafeDisposeAsync();
            throw;
        }
    }

    private async Task TryProcessTcpClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        try {
            await ProcessTcpClient(tcpClient, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            if (ex is ISelfLog loggable) loggable.Log();
            else
                VhLogger.Instance.LogDebug(GeneralEventId.Request, ex,
                    "ServerHost could not process this request. ClientStreamId: {ClientStreamId}, RemoteEp: {RemoteEp}",
                    ex.Data["ClientStreamId"], tcpClient.Client.RemoteEndPoint);

            tcpClient.Dispose();
        }
    }

    private async Task ProcessTcpClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var timeoutCt = new CancellationTokenSource(_sessionManager.SessionOptions.TcpConnectTimeoutValue);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, cancellationToken);
        var clientStream = await CreateClientStream(tcpClient, connectCts.Token).Vhc();
        try {
            await ProcessClientStream(clientStream, timeoutCts: null, connectCts.Token).Vhc();
        }
        catch (Exception ex) {
            // try to write unauthorized response for any unknown error if client steam is not ChunkStream
            // ReSharper disable once AccessToDisposedClosure
            if (clientStream is { Connected: true, RequireHttpResponse: true } && !VhUtils.IsSocketClosedException(ex)) {
                VhLogger.Instance.LogDebug(GeneralEventId.Request,
                    "ServerHost is writing unauthorized response. ClientStreamId: {ClientStreamId}, RemoteEp: {RemoteEp}",
                    clientStream.ClientStreamId, clientStream.IpEndPointPair.RemoteEndPoint);

                await VhUtils.TryInvokeAsync("Write unauthorized response.",
                    () => clientStream.Stream.WriteAsync(HttpResponseBuilder.Unauthorized(), connectCts.Token)).Vhc();
            }

            // dispose the stream
            clientStream.DisposeWithoutReuse();
            ex.Data["ClientStreamId"] = clientStream.ClientStreamId;
            throw;
        }
    }

    private void ReuseClientStream(IClientStream clientStream)
    {
        // it handles the exception and dispose the stream
        _ = TryReuseClientStreamAsync(clientStream);
    }

    private async Task TryReuseClientStreamAsync(IClientStream clientStream)
    {
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // add client stream to reuse list and take the ownership
            using var timeoutCts = new CancellationTokenSource(_sessionManager.SessionOptions.TcpReuseTimeoutValue);
            using var reusedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cancellationTokenSource.Token);

            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "ServerHost is pending a shared ClientStream for reuse. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);

            await ProcessClientStream(clientStream, timeoutCts, reusedCts.Token).Vhc();

            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "ServerHost has reused a shared ClientStream. ClientStreamId: {ClientStreamId}",
                    clientStream.ClientStreamId);
        }
        catch (Exception ex) {
            clientStream.DisposeWithoutReuse();
            if (ex is ISelfLog loggable) loggable.Log();
            else
                VhLogger.Instance.LogDebug(GeneralEventId.Request, ex,
                    "ServerHost could not process this request on reused stream. ClientStreamId: {ClientStreamId}",
                    clientStream.ClientStreamId);
        }
    }

    private async Task ProcessClientStream(IClientStream clientStream,
        CancellationTokenSource? timeoutCts, CancellationToken cancellationToken)
    {
        using var scope = VhLogger.Instance.BeginScope(
            $"RemoteEp: {VhLogger.Format(clientStream.IpEndPointPair.RemoteEndPoint)}");

        try {
            // Take ownership: stream is added here and removed in finally
            lock (_clientStreams)
                _clientStreams.Add(clientStream);

            await ProcessRequest(clientStream, timeoutCts, cancellationToken).Vhc();
        }
        catch (SessionException ex) {
            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogDebug(GeneralEventId.Session, ex,
                    "Could not process this request. SessionErrorCode: {SessionErrorCode}, ClientStreamId: {ClientStreamId}",
                    ex.SessionResponse.ErrorCode, clientStream.ClientStreamId);

            // reply the error to caller if it is SessionException
            // Should not reply anything when user is unknown. ServerUnauthorizedException will be thrown when user is unauthenticated
            await clientStream.DisposeAsync(ex.SessionResponse, cancellationToken).Vhc();
        }
        finally {
            lock (_clientStreams)
                _clientStreams.Remove(clientStream);
        }
    }

    private async Task ProcessRequest(IClientStream clientStream, CancellationTokenSource? timeoutCts, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];

        // read request version. We may wait here for reuse timeout
        var rest = await clientStream.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).Vhc();
        if (rest == 0)
            throw new EndOfStreamException("ClientStream has been closed before receiving any request.");

        // a new request is coming, reset timeout, to reset the reuse timeout if it is a reused stream
        timeoutCts?.CancelAfter(_sessionManager.SessionOptions.TcpConnectTimeoutValue);

        var version = buffer[0];
        if (version != 1)
            throw new EndOfStreamException($"The request version is not supported. Version: {version}");

        // read request code
        var res = await clientStream.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).Vhc();
        if (res == 0)
            throw new Exception("ClientStream has been closed before reading the request code.");

        var requestCode = (RequestCode)buffer[0];
        switch (requestCode) {
            case RequestCode.Hello:
                await ProcessHello(clientStream, cancellationToken).Vhc();
                break;

            case RequestCode.SessionStatus:
                await ProcessSessionStatus(clientStream, cancellationToken).Vhc();
                break;

            case RequestCode.TcpPacketChannel:
                await ProcessTcpPacketChannel(clientStream, cancellationToken).Vhc();
                break;

            case RequestCode.ProxyChannel:
                await ProcessStreamProxyChannel(clientStream, cancellationToken).Vhc();
                break;

            case RequestCode.UdpPacket:
                await ProcessUdpPacketRequest(clientStream, cancellationToken).Vhc();
                break;

            case RequestCode.RewardedAd:
                await ProcessRewardedAdRequest(clientStream, cancellationToken).Vhc();
                break;

            case RequestCode.ServerCheck:
                await ProcessServerCheck(clientStream, cancellationToken).Vhc();
                break;

            case RequestCode.Bye:
                await ProcessBye(clientStream, cancellationToken).Vhc();
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

        var request = await StreamUtils.ReadObjectAsync<T>(clientStream.Stream, cancellationToken).Vhc();

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

        var request = await ReadRequest<HelloRequest>(clientStream, cancellationToken).Vhc();

        // find best version (for future)
        var protocolVersion = Math.Min(MaxProtocolVersion, request.ClientInfo.MaxProtocolVersion);

        // creating a session
        VhLogger.Instance.LogDebug(GeneralEventId.Session,
            "Creating a session... TokenId: {TokenId}, ClientId: {ClientId}, ClientVersion: {ClientVersion}, UserAgent: {UserAgent}",
            VhLogger.FormatId(request.TokenId), VhLogger.FormatId(request.ClientInfo.ClientId),
            request.ClientInfo.ClientVersion, request.ClientInfo.UserAgent);
        var sessionResponseEx = await _sessionManager.CreateSession(request, ipEndPointPair, protocolVersion).Vhc();
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

        await clientStream.DisposeAsync(helloResponse, cancellationToken).Vhc();
    }

    private async Task ProcessRewardedAdRequest(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading the RewardedAd request...");
        var request = await ReadRequest<RewardedAdRequest>(clientStream, cancellationToken).Vhc();
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).Vhc();
        await session.ProcessRewardedAdRequest(request, clientStream, cancellationToken).Vhc();
    }

    // todo: it must be removed
    private async Task ProcessSessionStatus(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading the SessionStatus request...");
        var request = await ReadRequest<SessionStatusRequest>(clientStream, cancellationToken).Vhc();

        // finding session
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).Vhc();

        // processing request
        await session.ProcessSessionStatusRequest(request, clientStream, cancellationToken).Vhc();
    }

    private static async Task ProcessServerCheck(IClientStream clientStream, CancellationToken cancellationToken)
    {
        // unauthorized reply is enough to prevent fingerprinting and let the client knows that the server is alive
        // no need to read the request, the header is enough to identify the request because bearer token exists in requests
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Process ServerCheck and return unauthorized for the request...");

        // write unauthorized response
        await clientStream.Stream.WriteAsync(HttpResponseBuilder.Unauthorized(), cancellationToken).Vhc();
        clientStream.DisposeWithoutReuse();
    }


    private async Task ProcessBye(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading the Bye request...");
        var request = await ReadRequest<ByeRequest>(clientStream, cancellationToken).Vhc();

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).Vhc();

        // Dispose client stream
        clientStream.PreventReuse();
        await clientStream.DisposeAsync(new SessionResponse { ErrorCode = SessionErrorCode.Ok }, cancellationToken);

        // must be last
        await _sessionManager.CloseSession(session.SessionId);
    }

    private async Task ProcessUdpPacketRequest(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Reading a UdpPacket request...");
        var request = await ReadRequest<UdpPacketRequest>(clientStream, cancellationToken).Vhc();

        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).Vhc();
        await session.ProcessUdpPacketRequest(request, clientStream, cancellationToken).Vhc();
    }

    private async Task ProcessTcpPacketChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Reading the TcpPacketChannelRequest...");
        var request = await ReadRequest<TcpPacketChannelRequest>(clientStream, cancellationToken).Vhc();

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).Vhc();
        await session.ProcessTcpPacketChannelRequest(request, clientStream, cancellationToken).Vhc();
    }

    private async Task ProcessStreamProxyChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Reading the StreamProxyChannelRequest...");
        var request = await ReadRequest<StreamProxyChannelRequest>(clientStream, cancellationToken).Vhc();

        // find session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair).Vhc();
        await session.ProcessTcpProxyRequest(request, clientStream, cancellationToken).Vhc();
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
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

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
            await VhUtils.RunTask(Task.WhenAll(tcpListenerTasks), TimeSpan.FromSeconds(15)).Vhc();
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