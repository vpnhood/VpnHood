using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Swan.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Messaging;

// ReSharper disable UnusedMember.Local

#nullable enable
namespace VpnHood.Test;

public class TestEmbedIoAccessServer : IDisposable
{
    private readonly IAccessServer _accessServer;
    private WebServer _webServer;

    public TestEmbedIoAccessServer(IAccessServer accessServer, bool autoStart = true)
    {
        try
        {
            Logger.UnregisterLogger<ConsoleLogger>();
        }
        catch
        {
            // ignored
        }

        _accessServer = accessServer;
        BaseUri = new Uri($"http://{Util.GetFreeEndPoint(IPAddress.Loopback)}");
        _webServer = CreateServer(BaseUri);
        if (autoStart)
            _webServer.Start();
    }

    public Uri BaseUri { get; }
    public IPEndPoint? RedirectHostEndPoint { get; set; }
    public HttpException? HttpException { get; set; }

    public void Dispose()
    {
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
        _webServer.Dispose();
    }


    private static async Task ResponseSerializerCallback(IHttpContext context, object? data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        context.Response.ContentType = MimeType.Json;
        await using var text = context.OpenResponseText(new UTF8Encoding(false));
        await text.WriteAsync(JsonSerializer.Serialize(data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private class ApiController : WebApiController
    {
        private readonly TestEmbedIoAccessServer _embedIoAccessServer;

        public ApiController(TestEmbedIoAccessServer embedIoAccessServer)
        {
            _embedIoAccessServer = embedIoAccessServer;
        }

        private IAccessServer AccessServer => _embedIoAccessServer._accessServer;

        protected override void OnBeforeHandler()
        {
            if (_embedIoAccessServer.HttpException != null)
                throw _embedIoAccessServer.HttpException;
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
        public async Task<SessionSessionResponseEx> Session_Get([QueryField] Guid serverId, uint sessionId,
            [QueryField] string hostEndPoint, [QueryField] string? clientIp)
        {
            _ = serverId;
            var res = await AccessServer.Session_Get(sessionId, IPEndPoint.Parse(hostEndPoint),
                clientIp != null ? IPAddress.Parse(clientIp) : null);
            return res;
        }

        [Route(HttpVerbs.Post, "/sessions")]
        public async Task<SessionSessionResponseEx> Session_Create([QueryField] Guid serverId)
        {
            _ = serverId;
            var sessionRequestEx = await GetRequestDataAsync<SessionRequestEx>();
            var res = await AccessServer.Session_Create(sessionRequestEx);
            if (_embedIoAccessServer.RedirectHostEndPoint != null &&
                !sessionRequestEx.HostEndPoint.Equals(_embedIoAccessServer.RedirectHostEndPoint))
            {
                res.RedirectHostEndPoint = _embedIoAccessServer.RedirectHostEndPoint;
                res.ErrorCode = SessionErrorCode.RedirectHost;
            }

            return res;
        }

        [Route(HttpVerbs.Post, "/sessions/{sessionId}/usage")]
        public async Task<SessionResponseBase> Session_AddUsage([QueryField] Guid serverId, uint sessionId, [QueryField] bool closeSession)
        {
            Console.WriteLine($"WW1: {sessionId}, {closeSession}");
            
            _ = serverId;
            try
            {
                var usageInfo = await GetRequestDataAsync<UsageInfo>();
                var res = closeSession
                    ? await AccessServer.Session_Close(sessionId, usageInfo)
                    : await AccessServer.Session_AddUsage(sessionId, usageInfo);
                return res;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"WW2: {sessionId}, {closeSession}, {ex}");
                throw;
            }

        }

        [Route(HttpVerbs.Get, "/certificates/{hostEndPoint}")]
        public Task<byte[]> GetSslCertificateData([QueryField] Guid serverId, string hostEndPoint)
        {
            _ = serverId;
            return AccessServer.GetSslCertificateData(IPEndPoint.Parse(hostEndPoint));
        }

        [Route(HttpVerbs.Post, "/status")]
        public async Task<ServerCommand> SendServerStatus([QueryField] Guid serverId)
        {
            _ = serverId;
            var serverStatus = await GetRequestDataAsync<ServerStatus>();
            return await AccessServer.Server_UpdateStatus(serverStatus);
        }


        [Route(HttpVerbs.Post, "/configure")]
        public async Task<ServerConfig> ServerConfigure([QueryField] Guid serverId)
        {
            _ = serverId;
            var serverInfo = await GetRequestDataAsync<ServerInfo>();
            return await AccessServer.Server_Configure(serverInfo);
        }
    }
}