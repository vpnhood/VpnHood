using System.Collections.Concurrent;
using System.Net;
using EmbedIO;
using EmbedIO.Actions;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Helper;

public class TestFileServer : IDisposable
{
    private readonly WebServer _webServer;
    private readonly ConcurrentDictionary<string, byte[]> _fileStore;
    public Uri ApiUrl { get; }
    public string? AuthorizationKey { get; }
    public string? AuthorizationValue { get; }
    public bool AllowPut { get; set; } = true;
    public bool AllowPost { get; set; } = true;

    public TestFileServer(string? authorizationKey, string? authorizationValue)
    {
        AuthorizationKey = authorizationKey;
        AuthorizationValue = authorizationValue;

        var endPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        ApiUrl = new Uri($"http://{endPoint}");

        // Create the in-memory file store
        _fileStore = new ConcurrentDictionary<string, byte[]>();

        // Create the web server
        _webServer = new WebServer(options => options.WithUrlPrefix(ApiUrl.ToString())
                .WithMode(HttpListenerMode.EmbedIO))
            .WithModule(new ActionModule("/", HttpVerbs.Any, async ctx => {

                // check authorization
                var method = Enum.Parse<HttpVerbs>(ctx.Request.HttpMethod, true);
                switch (method) {
                    case HttpVerbs.Get:
                        await HandleGet(ctx);
                        break;

                    case HttpVerbs.Post:
                        if (!AllowPost) {
                            ctx.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed; // Unauthorized
                            await ctx.Response.OutputStream.WriteAsync("Command not supported"u8.ToArray());
                            break;
                        }

                        if (!await ValidateAuthorization(ctx)) return;
                        await HandlePost(ctx);
                        break;

                    case HttpVerbs.Put:
                        if (!AllowPut) {
                            ctx.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed; // Unauthorized
                            await ctx.Response.OutputStream.WriteAsync("Command not supported"u8.ToArray());
                            break;
                        }

                        if (!await ValidateAuthorization(ctx)) return;
                        await HandlePut(ctx);
                        break;

                    default:
                        ctx.Response.StatusCode = 405; // Method Not Allowed
                        await ctx.Response.OutputStream.WriteAsync("Method not allowed"u8.ToArray());
                        break;
                }
            }));

        // Start the server
        _ = _webServer.RunAsync();
    }


    private async Task<bool> ValidateAuthorization(IHttpContext ctx)
    {
        if (AuthorizationKey == null && AuthorizationValue == null)
            return true;

        if (ctx.Request.Headers.Get(AuthorizationKey) == AuthorizationValue)
            return true;

        ctx.Response.StatusCode = 401; // Unauthorized
        await ctx.Response.OutputStream.WriteAsync("Unauthorized"u8.ToArray());
        return false;
    }

    private static string GetFileFromRequest(IHttpContext ctx)
    {
        return ctx.Request.Url.LocalPath;
    }

    private async Task HandleGet(IHttpContext ctx)
    {
        var fileName = GetFileFromRequest(ctx);
        if (string.IsNullOrWhiteSpace(fileName) || !_fileStore.TryGetValue(fileName, out var fileData)) {
            ctx.Response.StatusCode = 404; // Not Found
            await ctx.Response.OutputStream.WriteAsync("File not found"u8.ToArray());
            return;
        }

        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.ContentLength64 = fileData.Length;
        await ctx.Response.OutputStream.WriteAsync(fileData, 0, fileData.Length);
    }

    private async Task HandlePost(IHttpContext ctx)
    {
        var fileName = GetFileFromRequest(ctx);
        if (string.IsNullOrWhiteSpace(fileName)) {
            ctx.Response.StatusCode = 400; // Bad Request
            await ctx.Response.OutputStream.WriteAsync("Filename is required"u8.ToArray());
            return;
        }

        using var memoryStream = new MemoryStream();
        await ctx.Request.InputStream.CopyToAsync(memoryStream);
        var fileData = memoryStream.ToArray();

        if (!_fileStore.TryAdd(fileName, fileData)) {
            ctx.Response.StatusCode = 409; // Conflict
            await ctx.Response.OutputStream.WriteAsync("File already exists"u8.ToArray());
            return;
        }

        ctx.Response.StatusCode = 201; // Created
        await ctx.Response.OutputStream.WriteAsync("File created successfully"u8.ToArray());
    }

    private async Task HandlePut(IHttpContext ctx)
    {
        var fileName = GetFileFromRequest(ctx);
        if (string.IsNullOrWhiteSpace(fileName)) {
            ctx.Response.StatusCode = 400; // Bad Request
            await ctx.Response.OutputStream.WriteAsync("Filename is required"u8.ToArray());
            return;
        }

        using var memoryStream = new MemoryStream();
        await ctx.Request.InputStream.CopyToAsync(memoryStream);
        var fileData = memoryStream.ToArray();

        _fileStore[fileName] = fileData;

        ctx.Response.StatusCode = 200; // OK
        await ctx.Response.OutputStream.WriteAsync("File updated successfully"u8.ToArray());
    }

    public void Dispose()
    {
        _webServer.Dispose();
    }
}