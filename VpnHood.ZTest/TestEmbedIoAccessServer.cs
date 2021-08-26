using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

#nullable enable
namespace VpnHood.Test
{
    public class TestEmbedIoAccessServer : IDisposable
    {
        private WebServer _webServer;
        private readonly Uri _uriPrefix;
        private readonly IAccessServer _accessServer;

        public Uri BaseUri => new(_uriPrefix, "/api/");
        public IPEndPoint? RedirectHostEndPoint { get; set; }

        class ApiController : WebApiController
        {
            private IAccessServer AccessServer => _embedIoAccessServer._accessServer;
            private readonly TestEmbedIoAccessServer _embedIoAccessServer;
            public ApiController(TestEmbedIoAccessServer embedIoAccessServer)
            {
                _embedIoAccessServer = embedIoAccessServer;
            }
            private async Task<T> GetRequestDataAsync<T>()
            {
                var json = await HttpContext.GetRequestBodyAsStringAsync();
                var res = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (res == null)
                    throw new Exception($"The request expected to have a {typeof(T).Name} but it is null!");
                return res;
            }

            [Route(HttpVerbs.Post, "/sessions/{sessionId}")]
            public async Task<SessionResponseEx> Session_Get(Guid serverId, uint sessionId, string hostEndPoint, string? clientIp)
            {
                _ = serverId;
                var res = await AccessServer.Session_Get(sessionId, IPEndPoint.Parse(hostEndPoint), clientIp!=null ? IPAddress.Parse(clientIp): null);
                return res;
            }

            [Route(HttpVerbs.Post, "/sessions")] 
            public async Task<SessionResponseEx> Session_Create(Guid serverId)
            {
                _ = serverId;
                var sessionRequestEx = await GetRequestDataAsync<SessionRequestEx>();
                var res = await AccessServer.Session_Create(sessionRequestEx);
                if (_embedIoAccessServer.RedirectHostEndPoint != null && !sessionRequestEx.HostEndPoint.Equals(_embedIoAccessServer.RedirectHostEndPoint))
                {
                    res.RedirectHostEndPoint = _embedIoAccessServer.RedirectHostEndPoint;
                    res.ErrorCode = SessionErrorCode.RedirectHost;
                }
                return res;
            }

            [Route(HttpVerbs.Post, "/sessions/{sessionId}/usage")]
            public async Task<ResponseBase> Session_AddUsage(Guid serverId, uint sessionId, bool closeSession)
            {
                _ = serverId;
                var usageInfo = await GetRequestDataAsync<UsageInfo>();
                return await AccessServer.Session_AddUsage(sessionId, closeSession, usageInfo);
            }

            [Route(HttpVerbs.Get, "/ssl-certificates/{hostEndPoint}")]
            public Task<byte[]> GetSslCertificateData([QueryField] string serverId, string hostEndPoint)
            {
                _ = serverId;
                return AccessServer.GetSslCertificateData(IPEndPoint.Parse(hostEndPoint));
            }

            [Route(HttpVerbs.Post, "/server-status")]
            public async Task SendServerStatus([QueryField] Guid serverId)
            {
                _ = serverId;
                var serverStatus = await GetRequestDataAsync<ServerStatus>();
                await AccessServer.Server_SetStatus(serverStatus);
            }


            [Route(HttpVerbs.Post, "/server-subscribe")]
            public async Task ServerSubscribe([QueryField] string serverId)
            {
                _ = serverId;
                var serverInfo = await GetRequestDataAsync<ServerInfo>();
                await AccessServer.Server_Subscribe(serverInfo);
            }

        }

        public TestEmbedIoAccessServer(IAccessServer accessServer, bool autoStart = true)
        {
            try { Swan.Logging.Logger.UnregisterLogger<Swan.Logging.ConsoleLogger>(); } catch { }

            _accessServer = accessServer;
            _uriPrefix = new Uri($"http://{Util.GetFreeEndPoint(IPAddress.Loopback)}");
            _webServer = CreateServer(_uriPrefix);
            if (autoStart)
                _webServer.Start();
        }

        public void Start()
        {
            // create the server
            _webServer = CreateServer(_uriPrefix);
            _webServer.RunAsync();
        }

        private WebServer CreateServer(Uri url)
        {
            return new WebServer(url.ToString())
                .WithWebApi("/api", ResponseSerializerCallback, c => c.WithController(() => new ApiController(this)));
        }

        public void Stop()
        {
            _webServer.Dispose();
            _webServer = CreateServer(_uriPrefix);
        }


        private async Task ResponseSerializerCallback(IHttpContext context, object? data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            context.Response.ContentType = MimeType.Json;
            using var text = context.OpenResponseText(new UTF8Encoding(false));
            await text.WriteAsync(JsonSerializer.Serialize(data, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        public void Dispose()
        {
            _webServer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}


