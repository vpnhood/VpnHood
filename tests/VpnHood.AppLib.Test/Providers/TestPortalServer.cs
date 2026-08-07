using System.Net;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>
/// A scripted Portal REST API on a loopback HttpListener: answers each route
/// ("POST /billing/purchases") from a response script and records every request
/// (method, path, body, auth headers) so tests assert the real HTTP surface,
/// not internals. Failures are RFC 9457 problem+json, as the portal sends them.
/// </summary>
public sealed class TestPortalServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Dictionary<string, Queue<Script>> _scripts = [];

    public Uri BaseUrl { get; }
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>The marker for a 204 answer (DELETE /auth/sessions/current).</summary>
    public static readonly object NoContent = new();

    public sealed class RecordedRequest
    {
        /// <summary>"POST /billing/purchases" — method and path, as the routing table names it.</summary>
        public required string Route { get; init; }
        public required string Path { get; init; }
        public string? Query { get; init; }
        public JsonElement Body { get; init; }
        public string? Authorization { get; init; }
        public string? PortalToken { get; init; }
    }

    public sealed class ErrorScript
    {
        public required string Code { get; init; }
        public required string Detail { get; init; }
        public int StatusCode { get; init; } = 400;
    }

    private sealed class Script
    {
        public required object Value { get; init; }
        public int StatusCode { get; init; }
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

    /// <summary>
    /// Enqueue the next answer for a route: the resource itself, an ErrorScript,
    /// or NoContent. statusCode 0 lets the route decide (POST creates → 201).
    /// </summary>
    public void Enqueue(string route, object dataOrError, int statusCode = 0)
    {
        if (!_scripts.TryGetValue(route, out var queue)) {
            queue = new Queue<Script>();
            _scripts[route] = queue;
        }
        queue.Enqueue(new Script { Value = dataOrError, StatusCode = statusCode });
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
                await Handle(context);
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

    private async Task Handle(HttpListenerContext context)
    {
        var method = context.Request.HttpMethod.ToUpperInvariant();

        // the base url is the endpoint script itself, so the resource is whatever
        // follows it — the same PATH_INFO shape the real portal routes on
        var path = context.Request.Url?.AbsolutePath ?? "";
        if (path.StartsWith("/api.php", StringComparison.Ordinal))
            path = path["/api.php".Length..];
        var route = $"{method} {path}";

        var bodyText = await new StreamReader(context.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
        lock (Requests) {
            Requests.Add(new RecordedRequest {
                Route = route,
                Path = path,
                Query = context.Request.Url?.Query,
                Body = string.IsNullOrEmpty(bodyText) ? default : JsonSerializer.Deserialize<JsonElement>(bodyText),
                Authorization = context.Request.Headers["Authorization"],
                PortalToken = context.Request.Headers["X-Portal-Token"]
            });
        }

        Script? script = null;
        if (_scripts.TryGetValue(route, out var queue) && queue.Count > 0)
            script = queue.Dequeue();

        var (statusCode, payload, isProblem) = script switch {
            null => (500, Problem(500, "internal_error", $"No scripted response for route: {route}"), true),
            { Value: ErrorScript error } => (error.StatusCode,
                Problem(error.StatusCode, error.Code, error.Detail), true),
            { Value: var value } when ReferenceEquals(value, NoContent) => (204, null, false),
            { Value: var value } => (script.StatusCode != 0 ? script.StatusCode : method == "POST" ? 201 : 200,
                (object?)value, false)
        };

        context.Response.StatusCode = statusCode;
        if (payload == null) {
            context.Response.Close();
            return;
        }

        var responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        context.Response.ContentType = isProblem ? "application/problem+json" : "application/json";
        await context.Response.OutputStream.WriteAsync(responseBytes);
        context.Response.Close();
    }

    private static object Problem(int statusCode, string code, string detail)
    {
        return new {
            type = $"https://docs.vpnhood.com/portal-api/errors/{code}",
            title = code,
            status = statusCode,
            code,
            detail
        };
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        _listener.Close();
        _cancellationTokenSource.Dispose();
    }
}
