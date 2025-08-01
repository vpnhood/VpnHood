﻿using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Server;

public class Http01ChallengeService(IPAddress[] ipAddresses, string token, string keyAuthorization, TimeSpan timeout)
    : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new(timeout);
    private readonly List<TcpListener> _tcpListeners = [];
    private bool _disposed;
    public bool IsStarted { get; private set; }

    public void Start()
    {
        if (IsStarted) throw new InvalidOperationException("The HTTP-01 Challenge Service has already been started.");
        if (_disposed) throw new ObjectDisposedException(nameof(Http01ChallengeService));

        try {
            IsStarted = true;
            foreach (var ipAddress in ipAddresses) {
                var ipEndPoint = new IPEndPoint(ipAddress, 80);
                VhLogger.Instance.LogInformation("HTTP-01 Challenge Listener starting on {EndPoint}", ipEndPoint);

                var listener = new TcpListener(ipEndPoint);
                listener.Start();
                _tcpListeners.Add(listener);
                _ = AcceptTcpClient(listener, _cancellationTokenSource.Token);
            }
        }
        catch {
            Stop();
            throw;
        }
    }

    private async Task AcceptTcpClient(TcpListener tcpListener, CancellationToken cancellationToken)
    {
        while (IsStarted && !cancellationToken.IsCancellationRequested) {
            using var client = await tcpListener.AcceptTcpClientAsync(cancellationToken).Vhc();
            try {
                await HandleRequest(client, token, keyAuthorization, cancellationToken).Vhc();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // service is stopping
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.DnsChallenge, ex, "Could not process the HTTP-01 challenge.");
            }
        }
    }

    private static async Task HandleRequest(TcpClient client, string token, string keyAuthorization,
        CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var headers = await HttpUtil.ParseHeadersAsync(stream, cancellationToken).Vhc()
                      ?? throw new Exception("Connection has been closed before receiving any request.");

        if (!headers.Any()) return;
        var request = headers[HttpUtil.HttpRequestKey];
        var requestParts = request.Split(' ');
        var expectedUrl = $"/.well-known/acme-challenge/{token}";
        var isMatched = requestParts.Length > 1 && requestParts[0] == "GET" && requestParts[1] == expectedUrl;

        VhLogger.Instance.LogInformation(GeneralEventId.DnsChallenge,
            "HTTP Challenge. Request: {request}, IsMatched: {isMatched}", request, isMatched);

        var response = isMatched
            ? HttpResponseBuilder.Http01(keyAuthorization)
            : HttpResponseBuilder.NotFound();

        await stream.WriteAsync(response, cancellationToken).Vhc();
        await stream.FlushAsync(cancellationToken).Vhc();
    }

    // use dispose
    private void Stop()
    {
        if (!IsStarted || _disposed)
            return;

        foreach (var listener in _tcpListeners) {
            try {
                listener.Stop();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not stop HTTP-01 Challenge Listener. {EndPoint}",
                    listener.LocalEndpoint);
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