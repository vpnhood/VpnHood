using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using static VpnHood.Core.Server.Http01ChallengeHandler;

namespace VpnHood.Core.Server;

// port 80 of an address the server listens on; httpsPort is the server's TLS port there, where it redirects. Its
// connections count with the server's that haven't proven a key, as none of them proves one
public class Http01ChallengeHandler : IDisposable
{
    private readonly IPAddress _ipAddress;
    private readonly int _httpsPort;
    private readonly Http01KeyAuthorizationFunc _keyAuthorizationFunc;
    private readonly UnauthenticatedConnections _unauthenticatedConnections;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private TcpListener? _tcpListener;
    private bool _disposed;
    public bool IsStarted { get; private set; }
    public bool ThrottleRequests { get; set; } = true;

    public delegate Task<string> Http01KeyAuthorizationFunc(string token, CancellationToken cancellationToken);

    internal Http01ChallengeHandler(IPAddress ipAddress, int httpsPort, Http01KeyAuthorizationFunc keyAuthorizationFunc,
        UnauthenticatedConnections unauthenticatedConnections)
    {
        _ipAddress = ipAddress;
        _httpsPort = httpsPort;
        _keyAuthorizationFunc = keyAuthorizationFunc;
        _unauthenticatedConnections = unauthenticatedConnections;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStarted) throw new InvalidOperationException("The HTTP-01 Challenge Service has already been started.");

        IsStarted = true;
        var ipEndPoint = new IPEndPoint(_ipAddress, 80);
        VhLogger.Instance.LogInformation("HTTP-01 Challenge Listener starting on {EndPoint}", VhLogger.FormatId(ipEndPoint));

        _tcpListener = new TcpListener(ipEndPoint);
        _tcpListener.Start();
        _ = AcceptTcpClient(_tcpListener, _cancellationTokenSource.Token);
    }

    private async Task AcceptTcpClient(TcpListener tcpListener, CancellationToken cancellationToken)
    {
        while (IsStarted && !cancellationToken.IsCancellationRequested) {
            var client = await tcpListener.AcceptTcpClientAsync(cancellationToken).Vhc();
            _ = HandleRequest(client, cancellationToken);
        }
    }

    // a challenge gets its key authorization; anything else what HTTPS redirection answers, as on a web server that
    // serves HTTPS alone and keeps port 80 for its certificate
    private async Task HandleRequest(TcpClient client, CancellationToken cancellationToken)
    {
        var reset = false;
        IPEndPointPair? endPoints = null;
        try {
            endPoints = new IPEndPointPair(client.GetLocalEndPoint(), client.GetRemoteEndPoint());
            _unauthenticatedConnections.Add(endPoints.Value, client);
            var stream = client.GetStream();
            while (true) {
                var request = await WebFront.ReadRequest(stream, cancellationToken).Vhc();
                if (request == null || await TryAnswerChallenge(client, stream, request, cancellationToken).Vhc())
                    break;

                var next = await WebFront.RedirectToHttps(stream, request, _httpsPort, cancellationToken).Vhc();
                if (next == WebFrontNext.NextRequest)
                    continue;

                reset = next == WebFrontNext.Reset;
                break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // service is stopping
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.AcmeChallenge, ex, "Could not process a request on port 80.");
        }
        finally {
            if (endPoints != null)
                _unauthenticatedConnections.Remove(endPoints.Value);

            // a reset closes the socket on its own: the client's close would shut it down, which sends a FIN
            if (reset) {
                client.Client.LingerState = new LingerOption(true, 0);
                client.Client.Dispose();
            }
            else
                DrainArrived(client);
            client.Dispose();
        }
    }

    // a socket closed with unread data resets its connection, where a web server, which reads ahead, ends its own
    // with a FIN: what has already arrived is read first
    private static void DrainArrived(TcpClient client)
    {
        try {
            var buffer = new byte[4096];
            for (var total = 0; client.Available > 0 && total < 1024 * 1024;)
                total += client.Client.Receive(buffer);
        }
        catch (Exception) {
            // the connection is going anyway
        }
    }

    // the key authorization for a token the access manager knows; any other token gets the redirect, as does a
    // lookup while the same address has one going, which keeps a flood off the access manager
    private async Task<bool> TryAnswerChallenge(TcpClient client, Stream stream, HttpRequestHead request,
        CancellationToken cancellationToken)
    {
        const string basePath = "/.well-known/acme-challenge/";
        if (request.Method != "GET" || !request.Target.StartsWith(basePath, StringComparison.Ordinal))
            return false;

        using var lockResult = ThrottleRequests
            ? await AsyncLock.LockAsync(IPAddressUtil.GetDosKey(client.Client.GetRemoteEndPoint().Address),
                TimeSpan.Zero, cancellationToken).Vhc()
            : null;
        if (lockResult is { Succeeded: false })
            return false;

        string keyAuthorization;
        try {
            keyAuthorization = await _keyAuthorizationFunc(request.Target[basePath.Length..], cancellationToken).Vhc();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            var logLevel = ex is KeyNotFoundException or ApiException { StatusCode: (int)HttpStatusCode.NotFound }
                ? LogLevel.Debug // a token no challenge has, as a crawler's
                : LogLevel.Error;
            VhLogger.Instance.Log(logLevel, GeneralEventId.AcmeChallenge, ex,
                "Could not get the HTTP-01 challenge's key authorization.");
            return false;
        }

        if (string.IsNullOrEmpty(keyAuthorization))
            return false;

        await stream.WriteAsync(HttpResponseBuilder.Http01(keyAuthorization), cancellationToken).Vhc();
        await stream.FlushAsync(cancellationToken).Vhc();
        VhLogger.Instance.LogInformation(GeneralEventId.AcmeChallenge, "HTTP-01 challenge response sent.");
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        IsStarted = false;
        VhInvoker.TryInvoke("stop the HTTP-01 listener", LogLevel.Warning, () => _tcpListener?.Stop());
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();
    }
}