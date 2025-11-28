using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.Test.AccessManagers;

public class TestHttpAccessManagerServer : IDisposable
{
    private WebserverLite _webServer;
    private readonly bool _autoDisposeBaseAccessManager;

    public TestHttpAccessManagerServer(IAccessManager baseAccessManager,
        bool autoDisposeBaseAccessManager = true)
    {
        BaseAccessManager = baseAccessManager;
        _autoDisposeBaseAccessManager = autoDisposeBaseAccessManager;
        _webServer = CreateServer(BaseUri);
    }

    public IAccessManager BaseAccessManager { get; }
    public Uri BaseUri { get; } = new($"http://{VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback)}");
    public HttpStatusCode? HttpExceptionStatusCode { get; set; }

    private WebserverLite CreateServer(Uri url)
    {
        var settings = new WebserverSettings(url.Host, url.Port);
        var server = new WebserverLite(settings, DefaultRoute);

        server
            .AddRouteMapper(isDebugMode: true)
            .AddController(new ApiController(this));
        
        return server;
    }

    private async Task DefaultRoute(HttpContextBase ctx)
    {
        if (HttpExceptionStatusCode != null)
        {
            ctx.Response.StatusCode = (int)HttpExceptionStatusCode.Value;
            await ctx.Response.Send();
            return;
        }

        ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
        await ctx.Response.Send();
    }

    public void Start()
    {
        _webServer = CreateServer(BaseUri);
        _webServer.Start();
        VhLogger.Instance.LogInformation(GeneralEventId.Test,
            "TestHttpAccessManagerServer is listening to {BaseUri}", BaseUri);
    }

    public void Stop()
    {
        _webServer.Stop();
        VhLogger.Instance.LogInformation(GeneralEventId.Test,
            "TestHttpAccessManagerServer has stopped listening to {Uri}", BaseUri);

        // do not call dispose to prevent crash if there are active connections
        // _webServer.Dispose();
    }

    public void Dispose()
    {
        Stop();
        if (_autoDisposeBaseAccessManager)
            BaseAccessManager.Dispose();
    }

    private class ApiController(TestHttpAccessManagerServer httpAccessManagerServer) : ControllerBase
    {
        private IAccessManager AccessManager => httpAccessManagerServer.BaseAccessManager;

        public override void AddRoutes(IRouteMapper mapper)
        {
            const string baseUrl = "/api/agent/";

            mapper.AddStatic(HttpMethod.GET, baseUrl + "sessions", async ctx => {
                var res = await AccessManager.Session_GetAll();
                await ctx.SendJson(res);
            });

            mapper.AddParam(HttpMethod.GET, baseUrl + "sessions/{sessionId}", async ctx => {
                var sessionId = ctx.GetRouteParameter<ulong>("sessionId");
                var hostEndPoint = ctx.GetQueryParameter<string>("hostEndPoint");
                var clientIpStr = ctx.GetQueryParameter<string?>("clientIp", null);
                var res = await AccessManager.Session_Get(sessionId, IPEndPoint.Parse(hostEndPoint),
                    clientIpStr != null ? IPAddress.Parse(clientIpStr) : null);
                await ctx.SendJson(res);
            });

            mapper.AddStatic(HttpMethod.POST, baseUrl + "sessions", async ctx => {
                var sessionRequestEx = ctx.ReadJson<SessionRequestEx>();
                var res = await AccessManager.Session_Create(sessionRequestEx);
                await ctx.SendJson(res);
            });

            mapper.AddParam(HttpMethod.POST, baseUrl + "sessions/{sessionId}/usage", async ctx => {
                var sessionId = ctx.GetRouteParameter<ulong>("sessionId");
                var closeSession = ctx.GetQueryParameter<bool>("closeSession");
                var adData = ctx.GetQueryParameter<string?>("adData", null);
                var traffic = ctx.ReadJson<Traffic>();
                var res = closeSession
                    ? await AccessManager.Session_Close(sessionId, traffic)
                    : await AccessManager.Session_AddUsage(sessionId, traffic, adData);
                await ctx.SendJson(res);
            });

            mapper.AddStatic(HttpMethod.POST, baseUrl + "sessions/usages", async ctx => {
                var sessionUsages = ctx.ReadJson<SessionUsage[]>();
                var res = await AccessManager.Session_AddUsages(sessionUsages);
                await ctx.SendJson(res);
            });

            mapper.AddStatic(HttpMethod.POST, baseUrl + "status", async ctx => {
                var serverStatus = ctx.ReadJson<ServerStatus>();
                var res = await AccessManager.Server_UpdateStatus(serverStatus);
                await ctx.SendJson(res);
            });

            mapper.AddStatic(HttpMethod.POST, baseUrl + "configure", async ctx => {
                var serverInfo = ctx.ReadJson<ServerInfo>();
                var res = await AccessManager.Server_Configure(serverInfo);
                await ctx.SendJson(res);
            });

            mapper.AddStatic(HttpMethod.GET, baseUrl + "acme/http01_key_authorization", async ctx => {
                var token = ctx.GetQueryParameter<string>("token");
                var res = await AccessManager.Acme_GetHttp01KeyAuthorization(token);
                await ctx.SendJson(res);
            });
        }
    }
}