using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Server;

public class Http01ChallengeService(IPAddress[] ipAddresses) : IDisposable
{
    private readonly List<TcpListener> _tcpListeners = [];
    private bool _disposed;
    public bool IsStarted { get; private set; }

    public Task Start()
    {
        if (IsStarted) throw new InvalidOperationException("The HTTP-01 Challenge Service has already been started.");
        if (_disposed) throw new ObjectDisposedException(nameof(Http01ChallengeService));

        foreach (var ipAddress in ipAddresses)
        {
            var ipEndPoint = new IPEndPoint(ipAddress, 80);
            VhLogger.Instance.LogInformation("HTTP-01 Challenge Listener starting on {EndPoint}", ipEndPoint);

            var listener = new TcpListener(ipEndPoint);
            listener.Start();
            _tcpListeners.Add(listener);
        }

        var listenTasks = _tcpListeners.Select(AcceptTcpClient);
        return Task.WhenAll(listenTasks);
    }

    private async Task AcceptTcpClient(TcpListener tcpListener)
    {
        try
        {
            while (true)
            {
                using var client = await tcpListener.AcceptTcpClientAsync();
                //HandleRequest(client, token, keyAuthorization);
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not process HTTP-01 request.");
        }
    }

    private static byte[] BuildHttp01Response(string challengeCode)
    {
        var header =
            "HTTP/1.1 200\r\n" +
            "Server: Kestrel\r\n" +
            $"Date: {DateTime.UtcNow:r}\r\n" +
            "Content-Type: text/plain\r\n" +
            $"Content-Length: {challengeCode.Length}\r\n" +
            "Connection: close\r\n";

        var body = $"{challengeCode}\r\n";

        var ret = header + "\r\n" + body;
        return Encoding.UTF8.GetBytes(ret);
    }

    private static async Task HandleRequest(TcpClient client, string token, string keyAuthorization, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var headers = await HttpUtil.ParseHeadersAsync(stream, cancellationToken)
            ?? throw new Exception("Connection has been closed before receiving any request.");

        if (!headers.Any()) return;
        var requestPart = headers[HttpUtil.HttpRequestKey];
        var requestParts = requestPart.Split(' ');
        var url = "/ok";

        if (requestParts.Length > 1 && requestParts[0] == "GET" && requestParts[1] == url)
        {
            var response = BuildHttp01Response("sss");
            await stream.WriteAsync(response, 0, response.Length, cancellationToken);
        }
    }

    public void Stop()
    {
        if (!IsStarted || _disposed) 
            return;

        foreach (var listener in _tcpListeners)
        {
            try
            {
                listener.Stop();
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not stop HTTP-01 Challenge Listener. {EndPoint}", listener.LocalEndpoint);
            }
        }

        _tcpListeners.Clear();
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
