using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Exceptions;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server;

internal class TcpHost : IAsyncDisposable
{
    private const int ServerProtocolVersion = 2;
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(60);
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly SessionManager _sessionManager;
    private readonly SslCertificateManager _sslCertificateManager;
    private readonly List<TcpListener> _tcpListeners = new();
    private bool _isIpV6Supported;
    private Task? _startTask;
    private bool _disposed;

    public bool IsStarted { get; private set; }

    public TcpHost(SessionManager sessionManager, SslCertificateManager sslCertificateManager)
    {
        _sslCertificateManager = sslCertificateManager ?? throw new ArgumentNullException(nameof(sslCertificateManager));
        _sessionManager = sessionManager;
    }

    public void Start(IPEndPoint[] tcpEndPoints, bool isIpV6Supported)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        if (IsStarted) throw new Exception($"{nameof(TcpHost)} is already Started!");
        if (tcpEndPoints.Length == 0) throw new Exception("No TcpEndPoint has been configured!");

        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;
        IsStarted = true;
        _isIpV6Supported = isIpV6Supported;

        try
        {
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
        catch
        {
            IsStarted = false;
            throw;
        }
    }

    public async Task Stop()
    {
        VhLogger.Instance.LogTrace($"Stopping {nameof(TcpHost)}...");
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
                _ = ProcessClient(tcpClient, cancellationToken);
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
                VhLogger.Instance.LogError(GeneralEventId.Tcp, ex, "TcpHost could not AcceptTcpClient. ErrorCounter: {ErrorCounter}", errorCounter);
                if (errorCounter > maxErrorCount)
                {
                    VhLogger.Instance.LogError("Too many unexpected errors in AcceptTcpClient. Stopping the TcpHost...");
                    _ = Stop();
                }
            }
        }

        tcpListener.Stop();
        VhLogger.Instance.LogInformation($"Listening on {VhLogger.Format(localEp)} has been stopped.");
    }

    private static async Task<SslStream> AuthenticateAsServerAsync(TcpClient tcpClient, X509Certificate certificate,
        CancellationToken cancellationToken)
    {
        try
        {

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
            return sslStream;
        }
        catch (Exception ex)
        {
            throw new TlsAuthenticateException("TLS Authentication error.", ex);
        }
    }

    private async Task ProcessClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var scope = VhLogger.Instance.BeginScope($"RemoteEp: {VhLogger.Format(tcpClient.Client.RemoteEndPoint)}");

        // add timeout to cancellationToken
        using var timeoutCt = new CancellationTokenSource(_requestTimeout);
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCt.Token, cancellationToken);
        cancellationToken = cancellationTokenSource.Token;
        TcpClientStream? tcpClientStream = null;
        var succeeded = false;

        try
        {
            // find certificate by ip 
            var certificate = await _sslCertificateManager.GetCertificate((IPEndPoint)tcpClient.Client.LocalEndPoint);

            // establish SSL
            var sslStream = await AuthenticateAsServerAsync(tcpClient, certificate, cancellationToken);

            // process request
            tcpClientStream = new TcpClientStream(tcpClient, sslStream);
            await ProcessRequest(tcpClientStream, cancellationToken);
            succeeded = true;
        }
        catch (ObjectDisposedException)
        {
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "Connection has been closed.");
        }
        catch (TlsAuthenticateException ex) when (ex.InnerException is OperationCanceledException)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Tls, "Client TLS authentication has been canceled.");
        }
        catch (TlsAuthenticateException ex)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Tls, ex, "Error in Client TLS authentication.");
        }
        catch (SessionException ex) when (tcpClientStream != null)
        {
            // reply the error to caller if it is SessionException
            // Should not reply anything when user is unknown
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, new SessionResponseBase(ex.SessionResponseBase),
                cancellationToken);

            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(ex.SessionResponseBase.ErrorCode == SessionErrorCode.GeneralError ? GeneralEventId.Tcp : GeneralEventId.Session, ex,
                    "Could not process the request. SessionErrorCode: {SessionErrorCode}", ex.SessionResponseBase.ErrorCode);
        }
        catch (Exception ex) when (tcpClientStream != null)
        {
            // return 401 for ANY non SessionException to keep server's anonymity
            // Server should always return 404 as error
            var unauthorizedResponse =
                "HTTP/1.1 401 Unauthorized\r\n" + "Content-Length: 0\r\n" +
                $"Date: {DateTime.UtcNow:r}\r\n" +
                "Server: Kestrel\r\n" + "WWW-Authenticate: Bearer\r\n";

            await tcpClientStream.Stream.WriteAsync(Encoding.UTF8.GetBytes(unauthorizedResponse), cancellationToken);

            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(GeneralEventId.Tcp, ex, "Could not process the request and return 401.");
        }
        catch (Exception ex)
        {
            if (ex is ISelfLog loggable)
                loggable.Log();
            else
                VhLogger.Instance.LogInformation(ex, "TcpHost could not process this request.");
        }
        finally
        {
            if (!succeeded)
            {
                tcpClientStream?.Dispose();
                tcpClient.Dispose();
            }
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
        {
            throw new NotSupportedException("The request version is not supported!");
        }

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

            case RequestCode.Bye:
                await ProcessBye(tcpClientStream, cancellationToken);
                break;

            default:
                throw new NotSupportedException("Unknown requestCode!");
        }
    }

    private async Task ProcessHello(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
    {
        var ipEndPointPair = tcpClientStream.IpEndPointPair;

        // reading request
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Processing hello request... ClientIp: {ClientIp}",
            VhLogger.Format(ipEndPointPair.RemoteEndPoint.Address));
        var request = await StreamUtil.ReadJsonAsync<HelloRequest>(tcpClientStream.Stream, cancellationToken);

        // creating a session
        VhLogger.Instance.LogTrace(GeneralEventId.Session, "Creating a session... TokenId: {TokenId}, ClientId: {ClientId}, ClientVersion: {ClientVersion}, UserAgent: {UserAgent}",
            VhLogger.FormatId(request.TokenId), VhLogger.FormatId(request.ClientInfo.ClientId), request.ClientInfo.ClientVersion, request.ClientInfo.UserAgent);
        var sessionResponse = await _sessionManager.CreateSession(request, ipEndPointPair);
        var session = _sessionManager.GetSessionById(sessionResponse.SessionId) ?? throw new InvalidOperationException("Session is lost!");
        session.UseUdpChannel = request.UseUdpChannel;

        // check client version; unfortunately it must be after CreateSession to preserver server anonymity
        if (request.ClientInfo == null || request.ClientInfo.ProtocolVersion < 2)
            throw new ServerSessionException(tcpClientStream.RemoteEndPoint, session, SessionErrorCode.UnsupportedClient,
                "This client is outdated and not supported anymore! Please update your app.");

        // Report new session
        var clientIp = _sessionManager.TrackingOptions.TrackClientIp ? VhLogger.Format(ipEndPointPair.RemoteEndPoint.Address) : "*";
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tTokenId: {TokenId}\tClientCount: {ClientCount,-3}\tClientId: {ClientId}\tClientIp: {ClientIp-15}\tVersion: {Version}\tOS: {OS}",
            VhLogger.FormatSessionId(session.SessionId), "New", VhLogger.FormatId(request.TokenId), session.SessionResponse.AccessUsage?.ActiveClientCount, VhLogger.FormatId(request.ClientInfo.ClientId), clientIp, request.ClientInfo.ClientVersion, UserAgentParser.GetOperatingSystem(request.ClientInfo.UserAgent));
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "SessionId: {SessionId}, Agent: {Agent}", VhLogger.FormatSessionId(session.SessionId), request.ClientInfo.UserAgent);

        //tracking
        if (_sessionManager.TrackingOptions.IsEnabled())
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
            UdpKey = session.UdpChannel?.Key,
            UdpPort = session.UdpChannel?.LocalPort ?? 0,
            ServerVersion = _sessionManager.ServerVersion,
            ServerProtocolVersion = ServerProtocolVersion,
            SuppressedTo = sessionResponse.SuppressedTo,
            AccessUsage = sessionResponse.AccessUsage,
            MaxDatagramChannelCount = session.Tunnel.MaxDatagramChannelCount,
            ClientPublicAddress = ipEndPointPair.RemoteEndPoint.Address,
            IsIpV6Supported = _isIpV6Supported,
            ErrorCode = SessionErrorCode.Ok
        };
        await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, helloResponse, cancellationToken);
        tcpClientStream.Dispose();
    }

    private async Task ProcessUdpChannel(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.Udp, "Reading a TcpDatagramChannel request...");
        var request = await StreamUtil.ReadJsonAsync<UdpChannelRequest>(tcpClientStream.Stream, cancellationToken);

        // finding session
        var session = await _sessionManager.GetSession(request, tcpClientStream.IpEndPointPair);

        // enable udp
        session.UseUdpChannel = true;

        if (session.UdpChannel == null)
            throw new InvalidOperationException("UdpChannel is not initialized.");

        // send OK reply
        await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, new UdpChannelSessionResponse(session.SessionResponse)
        {
            UdpKey = session.UdpChannel.Key,
            UdpPort = session.UdpChannel.LocalPort
        }, cancellationToken);

        tcpClientStream.Dispose();
    }

    private async Task ProcessBye(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel, $"Reading the {RequestCode.Bye} request ...");
        var request = await StreamUtil.ReadJsonAsync<RequestBase>(tcpClientStream.Stream, cancellationToken);

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, tcpClientStream.IpEndPointPair);

        // Before calling CloseSession session must be validated by GetSession
        await _sessionManager.CloseSession(session.SessionId);
        tcpClientStream.Dispose();
    }

    private async Task ProcessTcpDatagramChannel(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel, $"Reading the {nameof(TcpDatagramChannelRequest)} ...");
        var request = await StreamUtil.ReadJsonAsync<TcpDatagramChannelRequest>(tcpClientStream.Stream, cancellationToken);

        // finding session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, tcpClientStream.IpEndPointPair);

        // send OK reply
        await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, session.SessionResponse, cancellationToken);

        // Disable UdpChannel
        session.UseUdpChannel = false;

        // add channel
        VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel, $"Creating a {nameof(TcpDatagramChannel)} channel. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
        var channel = new TcpDatagramChannel(tcpClientStream);
        try { session.Tunnel.AddChannel(channel); }
        catch { channel.Dispose(); throw; }
    }

    private async Task ProcessTcpProxyChannel(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.StreamChannel, $"Reading the {nameof(TcpProxyChannelRequest)} ...");
        var request = await StreamUtil.ReadJsonAsync<TcpProxyChannelRequest>(tcpClientStream.Stream, cancellationToken);

        // find session
        using var scope = VhLogger.Instance.BeginScope($"SessionId: {VhLogger.FormatSessionId(request.SessionId)}");
        var session = await _sessionManager.GetSession(request, tcpClientStream.IpEndPointPair);
        await session.ProcessTcpChannelRequest(tcpClientStream, request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Stop();
    }
}
