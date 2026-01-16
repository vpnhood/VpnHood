using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Utils;
using static VpnHood.Core.Server.Http01ChallengeHandler;

namespace VpnHood.Core.Server;

public class Http01ChallengeHandler(IPAddress ipAddress, Http01KeyAuthorizationFunc keyAuthorizationFunc)
    : IDisposable
{
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private TcpListener? _tcpListener;
    private bool _disposed;
    public bool IsStarted { get; private set; }
    public delegate Task<string> Http01KeyAuthorizationFunc(string token);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStarted) throw new InvalidOperationException("The HTTP-01 Challenge Service has already been started.");

        IsStarted = true;
        var ipEndPoint = new IPEndPoint(ipAddress, 80);
        VhLogger.Instance.LogInformation("HTTP-01 Challenge Listener starting on {EndPoint}", ipEndPoint);

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

    private async Task HandleRequest(TcpClient client, CancellationToken cancellationToken)
    {
        // prevent duplicate request from each ip if too many requests come in
        using var lockAsyncResult = await AsyncLock.LockAsync(
            IPAddressUtil.GetDosKey(client.Client.GetRemoteEndPoint().Address), TimeSpan.Zero, cancellationToken);
        if (!lockAsyncResult.Succeeded)
            throw new HttpRequestException("Too many requests from the same IP address.", null, HttpStatusCode.TooManyRequests);

        try {
            using var timeoutCt = new CancellationTokenSource(_requestTimeout);
            using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCt.Token);
            await HandleRequestCore(client, linkedCt.Token).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // service is stopping
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.AcmeChallenge, ex, "Could not process the HTTP-01 challenge.");
        }
        finally {
            client.Dispose();
        }
    }

    private async Task HandleRequestCore(TcpClient client, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.AcmeChallenge,
            "Receiving an HTTP request from {RemoteIp}...", client.Client.GetRemoteEndPoint());
        await using var stream = client.GetStream();
        var headers = await HttpUtils.ParseHeadersAsync(stream, cancellationToken).Vhc()
                      ?? throw new Exception("Connection has been closed before receiving any request.");

        if (!headers.Any()) return;
        var request = headers[HttpUtils.HttpRequestKey];
        var requestParts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2)
            throw new HttpRequestException(
                $"Invalid HTTP-01 challenge request. RemoteIdp: {client.Client.GetRemoteEndPoint()}. Request: {request}",
                null, HttpStatusCode.BadRequest);

        var method = requestParts[0];
        var path = requestParts[1];

        // Handle HEAD /
        if (method == "HEAD" && path == "/") {
            await stream.WriteAsync(HttpResponseBuilder.Ok(), cancellationToken).Vhc();
            await stream.FlushAsync(cancellationToken).Vhc();
            VhLogger.Instance.LogInformation(GeneralEventId.AcmeChallenge, "HEAD / responded with 200 OK.");
            return;
        }

        // Retrieve token from request in this format: GET /.well-known/acme-challenge/{token} HTTP/1.1
        const string basePath = "/.well-known/acme-challenge/";
        var token = method == "GET" && path.StartsWith(basePath)
            ? path[basePath.Length..]
            : throw new HttpRequestException(
                $"Invalid HTTP-01 challenge request. RemoteIdp: {client.Client.GetRemoteEndPoint()}. Request: {request}",
                null, HttpStatusCode.BadRequest);

        // Get key authorization
        VhLogger.Instance.LogInformation(GeneralEventId.AcmeChallenge, "HTTP Challenge. Request: {request}", request);
        var keyAuthorization = await GetKeyAuthorization(token, stream, cancellationToken);

        // Send response
        await stream.WriteAsync(HttpResponseBuilder.Http01(keyAuthorization), cancellationToken).Vhc();
        await stream.FlushAsync(cancellationToken).Vhc();
        VhLogger.Instance.LogInformation(GeneralEventId.AcmeChallenge, "HTTP-01 challenge response sent.");
    }

    private async Task<string> GetKeyAuthorization(string token, Stream outStream, CancellationToken cancellationToken)
    {
        try {
            var keyAuthorization = await keyAuthorizationFunc(token);
            return !string.IsNullOrEmpty(keyAuthorization)
                ? keyAuthorization
                : throw new KeyNotFoundException("Key authorization not found for token: " + token);
        }
        catch (Exception ex) {
            if (ex is NotSupportedException) ex = new KeyNotFoundException();
            await outStream.WriteAsync(HttpResponseBuilder.Error(ex), cancellationToken).Vhc();
            await outStream.FlushAsync(cancellationToken).Vhc();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        IsStarted = false;
        VhUtils.TryInvoke("Stop Http01 Listener", () => _tcpListener?.Stop());
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();
        _disposed = true;
    }
}