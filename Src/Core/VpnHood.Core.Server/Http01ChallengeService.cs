using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Utils;
using static VpnHood.Core.Server.Http01ChallengeService;

namespace VpnHood.Core.Server;

public class Http01ChallengeService(Http01KeyAuthorizationFunc keyAuthorizationFunc)
    : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<TcpListener> _tcpListeners = [];
    private bool _disposed;
    public bool IsStarted { get; private set; }
    public delegate Task<string> Http01KeyAuthorizationFunc(string token);

    public void Start(IPAddress[] ipAddresses, bool ignoreError)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStarted) throw new InvalidOperationException("The HTTP-01 Challenge Service has already been started.");

        IsStarted = true;
        foreach (var ipAddress in ipAddresses) {
            try {
                var ipEndPoint = new IPEndPoint(ipAddress, 80);
                VhLogger.Instance.LogInformation("HTTP-01 Challenge Listener starting on {EndPoint}", ipEndPoint);

                var listener = new TcpListener(ipEndPoint);
                listener.Start();
                _tcpListeners.Add(listener);
                _ = AcceptTcpClient(listener, _cancellationTokenSource.Token);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not start HTTP-01 Challenge Listener on {EndPoint}", ipAddress);
                if (!ignoreError)
                    throw;
            }
        }
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

    // use dispose
    public void Stop()
    {
        if (!IsStarted || _disposed)
            return;

        foreach (var listener in _tcpListeners) {
            try {
                listener.Stop();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not stop HTTP-01 Challenge Listener. {EndPoint}",
                    VhUtils.TryInvoke(()=> listener.LocalEndpoint));
            }
        }

        _tcpListeners.Clear();
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();
        IsStarted = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (IsStarted)
            Stop();

        _disposed = true;
    }
}