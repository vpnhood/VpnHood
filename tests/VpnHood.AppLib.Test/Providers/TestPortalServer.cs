using System.Net;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>
/// A scripted Portal API endpoint on a loopback HttpListener: answers each
/// action from a response script and records every request (action, body,
/// auth headers) so tests assert the real HTTP surface, not internals.
/// </summary>
public sealed class TestPortalServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Dictionary<string, Queue<object>> _scripts = [];

    public Uri BaseUrl { get; }
    public List<RecordedRequest> Requests { get; } = [];

    public sealed class RecordedRequest
    {
        public required string Action { get; init; }
        public required JsonElement Body { get; init; }
        public string? Authorization { get; init; }
        public string? PortalToken { get; init; }
    }

    public TestPortalServer()
    {
        var endPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        BaseUrl = new Uri($"http://{endPoint}/api.php");
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{endPoint}/");
        _listener.Start();
        _ = AcceptLoop();
    }

    /// <summary>Enqueue the next data payload (or, for a failure, an ErrorScript) for an action.</summary>
    public void Enqueue(string action, object dataOrError)
    {
        if (!_scripts.TryGetValue(action, out var queue)) {
            queue = new Queue<object>();
            _scripts[action] = queue;
        }
        queue.Enqueue(dataOrError);
    }

    public sealed class ErrorScript
    {
        public required string Error { get; init; }
        public int StatusCode { get; init; } = 400;
    }

    private async Task AcceptLoop()
    {
        while (!_cancellationTokenSource.IsCancellationRequested) {
            HttpListenerContext context;
            try {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) {
                return; // listener stopped
            }

            try {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var bodyText = await reader.ReadToEndAsync();
                var body = JsonSerializer.Deserialize<JsonElement>(bodyText);
                var action = body.GetProperty("action").GetString() ?? "";

                lock (Requests) {
                    Requests.Add(new RecordedRequest {
                        Action = action,
                        Body = body,
                        Authorization = context.Request.Headers["Authorization"],
                        PortalToken = context.Request.Headers["X-Portal-Token"]
                    });
                }

                object? script = null;
                if (_scripts.TryGetValue(action, out var queue) && queue.Count > 0)
                    script = queue.Dequeue();

                int statusCode;
                object payload;
                switch (script) {
                    case null:
                        statusCode = 400;
                        payload = new { success = false, error = $"No scripted response for action: {action}" };
                        break;
                    case ErrorScript error:
                        statusCode = error.StatusCode;
                        payload = new { success = false, error = error.Error };
                        break;
                    default:
                        statusCode = 200;
                        payload = new { success = true, data = script };
                        break;
                }

                var responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(responseBytes);
                context.Response.Close();
            }
            catch (Exception) {
                try {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch {
                    // the test is tearing down
                }
            }
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        _listener.Close();
        _cancellationTokenSource.Dispose();
    }
}
