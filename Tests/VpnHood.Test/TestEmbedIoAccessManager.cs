using System.Net;
using System.Text;
using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Microsoft.Extensions.Logging;
using Swan.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.Access.Messaging;
using VpnHood.Tunneling;

// ReSharper disable UnusedMember.Local

namespace VpnHood.Test;

public class TestEmbedIoAccessManager : IDisposable
{
    private WebServer _webServer;

    public IAccessManager FileAccessManager { get; }


    public TestEmbedIoAccessManager(IAccessManager fileFileAccessManager, bool autoStart = true)
    {
        try { Logger.UnregisterLogger<ConsoleLogger>(); } catch { /* ignored */}

        FileAccessManager = fileFileAccessManager;
        BaseUri = new Uri($"http://{VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback)}");
        _webServer = CreateServer(BaseUri);
        if (autoStart)
        {
            _webServer.Start();
            VhLogger.Instance.LogInformation(GeneralEventId.Test, $"{VhLogger.FormatType(this)} is listening to {BaseUri}");
        }
    }

    public Uri BaseUri { get; }
    public IPEndPoint? RedirectHostEndPoint { get; set; }
    public HttpException? HttpException { get; set; }
    public Dictionary<string, IPEndPoint?> ServerLocations { get; set; } = new();

    public void Dispose()
    {
        Stop();
        _webServer.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        // create the server
        _webServer = CreateServer(BaseUri);
        _webServer.RunAsync();
    }

    private WebServer CreateServer(Uri url)
    {
        return new WebServer(url.ToString())
            .WithWebApi("/api/agent", ResponseSerializerCallback, c => c.WithController(() => new ApiController(this)));
    }

    public void Stop()
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, $"{VhLogger.FormatType(this)} has stopped listening to {BaseUri}");
        _webServer.Dispose();
    }


    private static async Task ResponseSerializerCallback(IHttpContext context, object? data)
    {
        ArgumentNullException.ThrowIfNull(data);

        context.Response.ContentType = MimeType.Json;
        await using var text = context.OpenResponseText(new UTF8Encoding(false));
        await text.WriteAsync(JsonSerializer.Serialize(data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private class ApiController(TestEmbedIoAccessManager embedIoAccessManager) : WebApiController
    {
        private IAccessManager AccessManager => embedIoAccessManager.FileAccessManager;

        protected override void OnBeforeHandler()
        {
            if (embedIoAccessManager.HttpException != null)
                throw embedIoAccessManager.HttpException;
            base.OnBeforeHandler();
        }

        private async Task<T> GetRequestDataAsync<T>()
        {
            var json = await HttpContext.GetRequestBodyAsStringAsync();
            var res = JsonSerializer.Deserialize<T>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (res == null)
                throw new Exception($"The request expected to have a {typeof(T).Name} but it is null!");
            return res;
        }

        [Route(HttpVerbs.Get, "/sessions/{sessionId}")]
        public async Task<SessionResponseEx> Session_Get([QueryField] Guid serverId, ulong sessionId,
            [QueryField] string hostEndPoint, [QueryField] string? clientIp)
        {
            _ = serverId;
            var res = await AccessManager.Session_Get(sessionId, IPEndPoint.Parse(hostEndPoint),
                clientIp != null ? IPAddress.Parse(clientIp) : null);
            return res;
        }

        [Route(HttpVerbs.Post, "/sessions")]
        public async Task<SessionResponseEx> Session_Create([QueryField] Guid serverId)
        {
            _ = serverId;
            var sessionRequestEx = await GetRequestDataAsync<SessionRequestEx>();
            var res = await AccessManager.Session_Create(sessionRequestEx);

            if (!sessionRequestEx.AllowRedirect)
                return res;
            
            if (embedIoAccessManager.RedirectHostEndPoint != null &&
                !sessionRequestEx.HostEndPoint.Equals(embedIoAccessManager.RedirectHostEndPoint))
            {
                res.RedirectHostEndPoint = embedIoAccessManager.RedirectHostEndPoint;
                res.ErrorCode = SessionErrorCode.RedirectHost;
            }

            // manage region
            if (sessionRequestEx.ServerLocation != null)
            {
                var redirectEndPoint = embedIoAccessManager.ServerLocations[sessionRequestEx.ServerLocation];
                if (!sessionRequestEx.HostEndPoint.Equals(redirectEndPoint))
                {
                    res.RedirectHostEndPoint = embedIoAccessManager.ServerLocations[sessionRequestEx.ServerLocation];
                    res.ErrorCode = SessionErrorCode.RedirectHost;
                }
            }

            return res;
        }

        [Route(HttpVerbs.Post, "/sessions/{sessionId}/usage")]
        public async Task<SessionResponse> Session_AddUsage([QueryField] Guid serverId, ulong sessionId,
            [QueryField] bool closeSession, [QueryField] string? adData)
        {
            _ = serverId;
            var traffic = await GetRequestDataAsync<Traffic>();
            var res = closeSession
                ? await AccessManager.Session_Close(sessionId, traffic)
                : await AccessManager.Session_AddUsage(sessionId, traffic, adData);
            return res;

        }

        [Route(HttpVerbs.Post, "/status")]
        public async Task<ServerCommand> SendServerStatus([QueryField] Guid serverId)
        {
            _ = serverId;
            var serverStatus = await GetRequestDataAsync<ServerStatus>();
            return await AccessManager.Server_UpdateStatus(serverStatus);
        }


        [Route(HttpVerbs.Post, "/configure")]
        public async Task<ServerConfig> ServerConfigure([QueryField] Guid serverId)
        {
            _ = serverId;
            var serverInfo = await GetRequestDataAsync<ServerInfo>();
            return await AccessManager.Server_Configure(serverInfo);
        }
    }
}