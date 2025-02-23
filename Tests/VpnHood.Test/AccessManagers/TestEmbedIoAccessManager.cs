using System.Net;
using System.Text;
using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Microsoft.Extensions.Logging;
using Swan.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Tunneling;

// ReSharper disable UnusedMember.Local

namespace VpnHood.Test.AccessManagers;

public class TestEmbedIoAccessManager : IDisposable
{
    private readonly bool _autoDisposeBaseAccessManager;
    private WebServer _webServer;

    public IAccessManager BaseAccessManager { get; }
    public Uri BaseUri { get; }
    public HttpException? HttpException { get; set; }

    public TestEmbedIoAccessManager(IAccessManager baseAccessManager, bool autoStart = true,
        bool autoDisposeBaseAccessManager = true)
    {
        _autoDisposeBaseAccessManager = autoDisposeBaseAccessManager;
        try {
            Logger.UnregisterLogger<ConsoleLogger>();
        }
        catch {
            /* ignored */
        }

        BaseAccessManager = baseAccessManager;
        BaseUri = new Uri($"http://{VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback)}");
        _webServer = CreateServer(BaseUri);
        if (autoStart) {
            _webServer.Start();
            VhLogger.Instance.LogInformation(GeneralEventId.Test,
                $"{VhLogger.FormatType(this)} is listening to {BaseUri}");
        }
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
        VhLogger.Instance.LogInformation(GeneralEventId.Test,
            $"{VhLogger.FormatType(this)} has stopped listening to {BaseUri}");
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

    public void Dispose()
    {
        Stop();
        _webServer.Dispose();
        if (_autoDisposeBaseAccessManager)
            BaseAccessManager.Dispose();
    }

    private class ApiController(TestEmbedIoAccessManager embedIoAccessManager) : WebApiController
    {
        private IAccessManager AccessManager => embedIoAccessManager.BaseAccessManager;

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

        [Route(HttpVerbs.Get, "/sessions")]
        public async Task<SessionResponseEx[]> Session_GetAll()
        {
            var res = await AccessManager.Session_GetAll();
            return res;
        }

        [Route(HttpVerbs.Get, "/sessions/{sessionId}")]
        public async Task<SessionResponseEx> Session_Get(ulong sessionId,
            [QueryField] string hostEndPoint, [QueryField] string? clientIp)
        {
            var res = await AccessManager.Session_Get(sessionId, IPEndPoint.Parse(hostEndPoint),
                clientIp != null ? IPAddress.Parse(clientIp) : null);
            return res;
        }

        [Route(HttpVerbs.Post, "/sessions")]
        public async Task<SessionResponseEx> Session_Create()
        {
            var sessionRequestEx = await GetRequestDataAsync<SessionRequestEx>();
            var res = await AccessManager.Session_Create(sessionRequestEx);
            return res;
        }

        [Route(HttpVerbs.Post, "/sessions/{sessionId}/usage")]
        public async Task<SessionResponse> Session_AddUsage(ulong sessionId,
            [QueryField] bool closeSession, [QueryField] string? adData)
        {
            var traffic = await GetRequestDataAsync<Traffic>();
            var res = closeSession
                ? await AccessManager.Session_Close(sessionId, traffic)
                : await AccessManager.Session_AddUsage(sessionId, traffic, adData);
            return res;
        }

        [Route(HttpVerbs.Post, "/sessions/usages")]
        public async Task<Dictionary<ulong, SessionResponse>> Session_AddUsages()
        {
            var sessionUsage = await GetRequestDataAsync<SessionUsage[]>();
            var res = await AccessManager.Session_AddUsages(sessionUsage);
            return res;
        }


        [Route(HttpVerbs.Post, "/status")]
        public async Task<ServerCommand> SendServerStatus()
        {
            var serverStatus = await GetRequestDataAsync<ServerStatus>();
            return await AccessManager.Server_UpdateStatus(serverStatus);
        }


        [Route(HttpVerbs.Post, "/configure")]
        public async Task<ServerConfig> ServerConfigure()
        {
            var serverInfo = await GetRequestDataAsync<ServerInfo>();
            return await AccessManager.Server_Configure(serverInfo);
        }
    }
}