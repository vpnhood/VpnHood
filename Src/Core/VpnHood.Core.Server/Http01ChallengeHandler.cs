using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Utils;
using static VpnHood.Core.Server.Http01ChallengeHandler;

namespace VpnHood.Core.Server;

public class Http01ChallengeHandler(IPAddress ipAddress, Http01KeyAuthorizationFunc keyAuthorizationFunc)
    : IDisposable
{
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
            using var client = await tcpListener.AcceptTcpClientAsync(cancellationToken).Vhc();
            try {
                await HandleRequest(client, keyAuthorizationFunc, cancellationToken).Vhc();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // service is stopping
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.DnsChallenge, ex, "Could not process the HTTP-01 challenge.");
            }
        }
    }

    private static async Task HandleRequest(TcpClient client, Http01KeyAuthorizationFunc keyAuthorizationFunc, 
        CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var headers = await HttpUtils.ParseHeadersAsync(stream, cancellationToken).Vhc()
                      ?? throw new Exception("Connection has been closed before receiving any request.");

        if (!headers.Any()) return;
        var request = headers[HttpUtils.HttpRequestKey];
        var requestParts = request.Split(' ');

        // Retrieve token from request in this format: GET /.well-known/acme-challenge/{token} HTTP/1.1
        const string basePath = "/.well-known/acme-challenge/";
        var token = requestParts.Length > 2 && requestParts[0] == "GET" && requestParts[1].StartsWith(basePath)
            ? requestParts[1][basePath.Length..]
            : throw new HttpRequestException("Invalid HTTP-01 challenge request.", null, HttpStatusCode.BadRequest);

        VhLogger.Instance.LogInformation(GeneralEventId.DnsChallenge, "HTTP Challenge. Request: {request}", request);
        try {
            var keyAuthorization = await keyAuthorizationFunc(token);
            await stream.WriteAsync(HttpResponseBuilder.Http01(keyAuthorization), cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.DnsChallenge, ex, "Could not get key authorization for token: {token}", token);
            await stream.WriteAsync(HttpResponseBuilder.NotFound(), cancellationToken).Vhc();
        }

        await stream.FlushAsync(cancellationToken).Vhc();
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