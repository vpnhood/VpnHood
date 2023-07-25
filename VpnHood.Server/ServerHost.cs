using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.JobController;
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

namespace VpnHood.Server;

internal class ServerHost : IAsyncDisposable, IJob
{
    private readonly HashSet<IClientStream> _clientStreams = new();
    private const int ServerProtocolVersion = 4;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly SessionManager _sessionManager;
    private readonly SslCertificateManager _sslCertificateManager;
    private readonly List<TcpListener> _tcpListeners = new();
    private readonly List<UdpChannelTransmitter> _udpChannelTransmitters = new();
    private Task? _startTask;
    private bool _disposed;

    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(5));
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? NetFilterPacketCaptureIncludeIpRanges { get; set; }
    public IpRange[]? NetFilterIncludeIpRanges { get; set; }
    public bool IsStarted { get; private set; }
    public IPEndPoint[] TcpEndPoints { get; private set; } = Array.Empty<IPEndPoint>();
    public IPEndPoint[] UdpEndPoints { get; private set; } = Array.Empty<IPEndPoint>();

    public ServerHost(SessionManager sessionManager, SslCertificateManager sslCertificateManager)
    {
        _sslCertificateManager = sslCertificateManager ?? throw new ArgumentNullException(nameof(sslCertificateManager));
        _sessionManager = sessionManager;
        JobRunner.Default.Add(this);
    }

    public void Start(IPEndPoint[] tcpEndPoints, IPEndPoint[] udpEndPoints)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        if (IsStarted) throw new Exception($"{nameof(ServerHost)} is already Started!");
        if (tcpEndPoints.Length == 0) throw new Exception("No TcpEndPoint has been configured!");

        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;
        IsStarted = true;
        lock (_stopLock) _stopTask = null;

        try
        {
            //start UDPs
            lock (_udpChannelTransmitters)
            {
                foreach (var udpEndPoint in udpEndPoints)
                {
                    if (udpEndPoint.Port != 0)
                        VhLogger.Instance.LogInformation("Start listening on UdpEndPoint: {UdpEndPoint}", VhLogger.Format(udpEndPoint));

                    var udpClient = new UdpClient(udpEndPoint);
                    var udpChannelTransmitter = new ServerUdpChannelTransmitter(udpClient, _sessionManager);
                    _udpChannelTransmitters.Add(udpChannelTransmitter);

                    if (udpEndPoint.Port == 0)
                        VhLogger.Instance.LogInformation("Start listening on UdpEndPoint: {UdpEndPoint}", VhLogger.Format(udpChannelTransmitter.LocalEndPoint));
                }

                UdpEndPoints = udpEndPoints;
            }

            //start TCPs
            var tasks = new List<Task>();
            lock (_tcpListeners)
            {
                foreach (var tcpEndPoint in tcpEndPoints)
                {
                    VhLogger.Instance.LogInformation("Start listening on TcpEndPoint: {TcpEndPoint}", VhLogger.Format(tcpEndPoint));
                    cancellationToken.ThrowIfCancellationRequested();
                    var tcpListener = new TcpListener(tcpEndPoint);
                    tcpListener.Start();
                    _tcpListeners.Add(tcpListener);
                    tasks.Add(ListenTask(tcpListener, cancellationToken));
                }
            }
            TcpEndPoints = tcpEndPoints;
            _startTask = Task.WhenAll(tasks);
        }
        catch
        {
            IsStarted = false;
            throw;
        }
    }

    private readonly AsyncLock _stopLock = new();
    private Task? _stopTask;

    public Task Stop()
    {
        lock (_stopLock)
            _stopTask ??= StopCore();

        return _stopTask;
    }

    private async Task StopCore()
    {
        if (!IsStarted)
            return;

        VhLogger.Instance.LogTrace("Stopping ServerHost...");
        _cancellationTokenSource.Cancel();
        _sslCertificateManager.ClearCache();

        // UDPs
        VhLogger.Instance.LogTrace("Disposing UdpChannelTransmitters...");
        lock (_udpChannelTransmitters)
        {
            foreach (var udpChannelClient in _udpChannelTransmitters)
                udpChannelClient.Dispose();
            _udpChannelTransmitters.Clear();
        }

        // TCPs
        VhLogger.Instance.LogTrace("Disposing TcpListeners...");
        lock (_tcpListeners)
        {
            foreach (var tcpListener in _tcpListeners)
                tcpListener.Stop();
            _tcpListeners.Clear();
        }

        // dispose clientStreams
        VhLogger.Instance.LogTrace("Disposing ClientStreams...");
        Task[] disposeTasks;
        lock (_clientStreams)
            disposeTasks = _clientStreams.Select(x => x.DisposeAsync(false).AsTask()).ToArray();
        await Task.WhenAll(disposeTasks);

        VhLogger.Instance.LogTrace("Disposing current processing requests...");
        try
        {
            if (_startTask != null)
                await _startTask;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogTrace(ex, "Error in stopping ServerHost.");
        }

        _startTask = null;
        IsStarted = false;
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
                var tcpClient = await tcpListener.AcceptTcpClientAsync();
                VhUtil.ConfigTcpClient(tcpClient,
                    _sessionManager.SessionOptions.TcpKernelSendBufferSize,
                    _sessionManager.SessionOptions.TcpKernelReceiveBufferSize);

                // config tcpClient
                _ = ProcessTcpClient(tcpClient, cancellationToken);
                errorCounter = 0;
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
                VhLogger.Instance.LogError(GeneralEventId.Tcp, ex, "ServerHost could not AcceptTcpClient. ErrorCounter: {ErrorCounter}", errorCounter);
                if (errorCounter > maxErrorCount)
                {
                    VhLogger.Instance.LogError("Too many unexpected errors in AcceptTcpClient. Stopping the ServerHost...");
                    _ = Stop();
                }
            }
        }

        tcpListener.Stop();
        VhLogger.Instance.LogInformation("TCP Listener has been stopped. LocalEp: {LocalEp}", VhLogger.Format(localEp));
    }

    private static async Task<SslStream> AuthenticateAsServerAsync(Stream stream, X509Certificate certificate,
        CancellationToken cancellationToken)
    {
        try
        {
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "TLS Authenticating. CertSubject: {CertSubject}...", certificate.Subject);
            var sslStream = new SslStream(stream, true);
            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                cancellationToken);
            return sslStream;
        }
        catch (Exception ex)
        {
            throw new TlsAuthenticateException("TLS Authentication error.", ex);
        }
    }

    private async Task<IClientStream> CreateClientStream(TcpClient tcpClient, Stream sslStream, CancellationToken cancellationToken)
    {
        // read version
        VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "Waiting for request...");
        var buffer = new byte[16];
        var res = await sslStream.ReadAsync(buffer, 0, 1, cancellationToken);
        if (res == 0)
            throw new Exception("Connection has been closed before receiving any request.");

        // check request version
        var streamId = Guid.NewGuid() + ":incoming";
        var version = buffer[0];

        //todo must be deprecated from >= 2.9.371
        if (version == 1)
            return new TcpClientStream(tcpClient, new ReadCacheStream(sslStream, false,
                cacheData: new[] { version }, cacheSize: 1), streamId);

        // Version 2 is HTTP and starts with POST
        try
        {
            var headers =
                await HttpUtil.ParseHeadersAsync(sslStream, cancellationToken)
                ?? throw new Exception("Connection has been closed before receiving any request.");

            // read api key
            var hasPassChecked = false;
            if (headers.TryGetValue("Authorization", out var authorization))
            {
                var parts = authorization.Split(' ');
                if (parts.Length >= 2 &&
                    parts[0].Equals("ApiKey", StringComparison.OrdinalIgnoreCase) &&
                    parts[1].Equals(_sessionManager.ApiKey, StringComparison.OrdinalIgnoreCase))
                {
                    var apiKey = parts[1];
                    hasPassChecked = apiKey == _sessionManager.ApiKey;
                }
            }

            if (hasPassChecked)
            {
                if (headers.TryGetValue("X-Version", out var xVersion) && int.Parse(xVersion) == 2 &&
                    headers.TryGetValue("X-Secret", out var xSecret))
                {
                    await sslStream.WriteAsync(HttpResponses.GetOk(), cancellationToken);
                    var secret = Convert.FromBase64String(xSecret);
                    await sslStream.DisposeAsync(); // dispose Ssl
                    return new TcpClientStream(tcpClient, new BinaryStream(tcpClient.GetStream(), streamId, secret), streamId, ReuseClientStream);
                }
            }

            throw new UnauthorizedAccessException();
        }
        catch
        {
            //always return BadRequest 
            if (!VhUtil.IsTcpClientHealthy(tcpClient)) throw;
            await sslStream.WriteAsync(HttpResponses.GetBadRequest(), cancellationToken);
            throw new Exception("Bad request.");
        }
    }

    private async Task ProcessTcpClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        // add timeout to cancellationToken
        using var timeoutCt = new CancellationTokenSource(TunnelDefaults.TcpRequestTimeout);
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, cancellationToken);
        cancellationToken = cancellationTokenSource.Token;

        IClientStream? clientStream = null;
        try
        {
            // find certificate by ip 
            var certificate = await _sslCertificateManager.GetCertificate((IPEndPoint)tcpClient.Client.LocalEndPoint);

            // establish SSL
            var sslStream = await AuthenticateAsServerAsync(tcpClient.GetStream(), certificate, cancellationToken);

            // create client stream
            clientStream = await CreateClientStream(tcpClient, sslStream, cancellationToken);
            lock (_clientStreams) _clientStreams.Add(clientStream);

            await ProcessClientStream(clientStream, cancellationToken);
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

            if (clientStream != null) await clientStream.DisposeAsync(false);
            tcpClient.Dispose();
        }
    }

    private async Task ReuseClientStream(IClientStream clientStream)
    {
        lock (_clientStreams) _clientStreams.Add(clientStream);
        using var timeoutCt = new CancellationTokenSource(TunnelDefaults.TcpReuseTimeout);
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

            await ProcessClientStream(clientStream, cancellationToken);

            VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                "ServerHost.ReuseClientStream: A shared ClientStream has been reused. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.Tcp, ex,
                "ServerHost.ReuseClientStream: Could not reuse a ClientStream. ClientStreamId: {ClientStreamId}",
                clientStream.ClientStreamId);

            await clientStream.DisposeAsync(false);
        }
    }

    private async Task ProcessClientStream(IClientStream clientStream, CancellationToken cancellationToken)
    {
        using var scope = VhLogger.Instance.BeginScope($"RemoteEp: {VhLogger.Format(clientStream.IpEndPointPair.RemoteEndPoint)}");
        try
        {
            await ProcessRequest(clientStream, cancellationToken);
        }
        catch (SessionException ex)
        {
            // reply the error to caller if it is SessionException
            // Should not reply anything when user is unknown
            await StreamUtil.WriteJsonAsync(clientStream.Stream, new SessionResponseBase(ex.SessionResponseBase),
                cancellationToken);

            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(ex.SessionResponseBase.ErrorCode == SessionErrorCode.GeneralError ? GeneralEventId.Tcp : GeneralEventId.Session, ex,
                    "Could not process the request. SessionErrorCode: {SessionErrorCode}", ex.SessionResponseBase.ErrorCode);

            await clientStream.DisposeAsync();
        }
        catch (Exception ex) when (VhLogger.IsSocketCloseException(ex))
        {
            VhLogger.LogError(GeneralEventId.Tcp, ex,
                "Connection has been closed. ClientStreamId: {ClientStreamId}.",
                clientStream.ClientStreamId);

            await clientStream.DisposeAsync();
        }
        catch (Exception ex)
        {
            // return 401 for ANY non SessionException to keep server's anonymity
            // Server should always return bad request as error
            //todo : deprecated from version 400 or upper
            await clientStream.Stream.WriteAsync(HttpResponses.GetBadRequest(), cancellationToken);

            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(GeneralEventId.Tcp, ex,
                    "Could not process the request and return 401. ClientStreamId: {ClientStreamId}", clientStream.ClientStreamId);

            await clientStream.DisposeAsync(false);
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
        var rest = await clientStream.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        if (rest == 0)
            throw new Exception("ClientStream has been closed before receiving any request.");

        var version = buffer[0];
        if (version != 1)
            throw new NotSupportedException($"The request version is not supported. Version: {version}");

        // read request code
        var res = await clientStream.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        if (res == 0)
            throw new Exception("ClientStream has been closed before receiving any request.");

        var requestCode = (RequestCode)buffer[0];
        switch (requestCode)
        {
            case RequestCode.Hello:
                await ProcessHello(clientStream, cancellationToken);
                break;

            case RequestCode.TcpDatagramChannel:
                await ProcessTcpDatagramChannel(clientStream, cancellationToken);
                break;

            case RequestCode.StreamProxyChannel:
                await ProcessStreamProxyChannel(clientStream, cancellationToken);
                break;

            case RequestCode.UdpChannel:
                await ProcessUdpChannel(clientStream, cancellationToken);
                break;

            case RequestCode.Bye:
                await ProcessBye(clientStream, cancellationToken);
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

        var request = await StreamUtil.ReadJsonAsync<T>(clientStream.Stream, cancellationToken);
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

        var request = await ReadRequest<HelloRequest>(clientStream, cancellationToken);

        // creating a session
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Creating a session... TokenId: {TokenId}, ClientId: {ClientId}, ClientVersion: {ClientVersion}, UserAgent: {UserAgent}",
            VhLogger.FormatId(request.TokenId), VhLogger.FormatId(request.ClientInfo.ClientId), request.ClientInfo.ClientVersion, request.ClientInfo.UserAgent);
        var sessionResponse = await _sessionManager.CreateSession(request, ipEndPointPair);
        var session = _sessionManager.GetSessionById(sessionResponse.SessionId) ?? throw new InvalidOperationException("Session is lost!");
        session.UseUdpChannel = request.UseUdpChannel;

        // check client version; unfortunately it must be after CreateSession to preserve server anonymity
        if (request.ClientInfo == null || request.ClientInfo.ProtocolVersion < 2) //todo: must be 3 or upper (4 for BinaryStream); 3 is deprecated by >= 2.9.371
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

        var helloResponse = new HelloSessionResponse(sessionResponse)
        {
            SessionId = sessionResponse.SessionId,
            SessionKey = sessionResponse.SessionKey,
            ServerSecret = _sessionManager.ServerSecret,
            TcpEndPoints = sessionResponse.TcpEndPoints,
            UdpEndPoints = sessionResponse.UdpEndPoints,
            GaMeasurementId = sessionResponse.GaMeasurementId,
            UdpKey = request.UseUdpChannel2 ? sessionResponse.SessionKey : session.UdpChannel?.Key,
            UdpPort = session.UdpChannel?.LocalPort ?? 0,
            ServerVersion = _sessionManager.ServerVersion,
            ServerProtocolVersion = ServerProtocolVersion,
            SuppressedTo = sessionResponse.SuppressedTo,
            AccessUsage = sessionResponse.AccessUsage,
            MaxDatagramChannelCount = session.Tunnel.MaxDatagramChannelCount,
            ClientPublicAddress = ipEndPointPair.RemoteEndPoint.Address,
            IncludeIpRanges = NetFilterIncludeIpRanges,
            PacketCaptureIncludeIpRanges = NetFilterPacketCaptureIncludeIpRanges,
            IsIpV6Supported = IsIpV6Supported,
            ErrorCode = SessionErrorCode.Ok
        };
        await StreamUtil.WriteJsonAsync(clientStream.Stream, helloResponse, cancellationToken);
        await clientStream.DisposeAsync();
    }

    private async Task ProcessUdpChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Udp, "Reading a TcpDatagramChannel request...");
        var request = await ReadRequest<UdpChannelRequest>(clientStream, cancellationToken);

        // finding session
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair);

        // enable udp
        session.UseUdpChannel = true;

        if (session.UdpChannel == null)
            throw new InvalidOperationException("UdpChannel is not initialized.");

        // send OK reply
        await StreamUtil.WriteJsonAsync(clientStream.Stream, new UdpChannelSessionResponse(session.SessionResponse)
        {
            UdpKey = session.UdpChannel.Key,
            UdpPort = session.UdpChannel.LocalPort
        }, cancellationToken);

        await clientStream.DisposeAsync();
    }

    private async Task ProcessBye(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Reading the Bye request ...");
        var request = await ReadRequest<ByeRequest>(clientStream, cancellationToken);

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair);

        // Before calling CloseSession session must be validated by GetSession
        await _sessionManager.CloseSession(session.SessionId);
        await clientStream.DisposeAsync(false);
    }

    private async Task ProcessTcpDatagramChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel, "Reading the TcpDatagramChannelRequest ...");
        var request = await ReadRequest<TcpDatagramChannelRequest>(clientStream, cancellationToken);

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair);
        await session.ProcessTcpDatagramChannelRequest(clientStream, request, cancellationToken);
    }

    private async Task ProcessStreamProxyChannel(IClientStream clientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.StreamProxyChannel, "Reading the StreamProxyChannelRequest ...");
        var request = await ReadRequest<StreamProxyChannelRequest>(clientStream, cancellationToken);

        // find session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, clientStream.IpEndPointPair);
        await session.ProcessTcpProxyRequest(clientStream, request, cancellationToken);
    }

    public Task RunJob()
    {
        lock (_clientStreams)
            _clientStreams.RemoveWhere(x => x.Disposed);

        return Task.CompletedTask;
    }

    private readonly AsyncLock _disposeLock = new();
    private ValueTask? _disposeTask;
    public async ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            _disposeTask ??= DisposeAsyncCore();
        await _disposeTask.Value;
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (_disposed) return;
        _disposed = true;

        await Stop();
    }

}
