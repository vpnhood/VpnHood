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
            var listener = new TcpListener(ipEndPoint);
            listener.Start();
            _tcpListeners.Add(listener);

        }
        var listenTasks = ipAddresses.Select(StartListener);
        return Task.WhenAll(listenTasks);
    }

    private TcpListener CreateListener(IPAddress ipAddress)
    {
        throw new AbandonedMutexException();
    }


    private async Task StartListener(IPAddress ipAddress)
    {
        // Start the TCP listener on port 80
        var ipEndPoint = new IPEndPoint(ipAddress, 80);
        var listener = new TcpListener(ipEndPoint);
        try
        {
            VhLogger.Instance.LogInformation("HTTP-01 Challenge Listener starting on {EndPoint}", ipEndPoint);
            listener.Start();
            _tcpListeners.Add(listener);
            while (true)
            {
                // Asynchronously wait for incoming connections
                var client = listener.AcceptTcpClientAsync();
                //HandleRequest(client, token, keyAuthorization);
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not HTTP-01 Challenge Listener starting on {EndPoint}", ipEndPoint);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleRequest(TcpClient client, string token, string keyAuthorization, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var headers = await HttpUtil.ParseHeadersAsync(stream, cancellationToken)
            ?? throw new Exception("Connection has been closed before receiving any request.");

        if (!headers.Any()) return;
        var requestPart = headers[HttpUtil.HttpRequestKey];
        var requestParts = requestPart.Split(' ');

        if (requestParts.Length > 1 && requestParts[0] == "GET")
        {
            var url = requestParts[1];
            // Check if the request URL matches the expected token URL
            if (url.EndsWith(token))
            {
                var response = $"HTTP/1.1 200 OK\r\nContent-Length: {keyAuthorization.Length}\r\nConnection: close\r\nContent-Type: text/plain\r\n\r\n{keyAuthorization}";
                var responseBytes = Encoding.ASCII.GetBytes(response);
                stream.Write(responseBytes, 0, responseBytes.Length);
            }
        }

        // Close the connection
        client.Close();
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
