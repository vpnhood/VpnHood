using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Server;

#nullable enable
namespace VpnHood.Test
{
    public class TestEmbedIoAccessServer : IDisposable
    {
        private WebServer _webServer;
        private readonly Uri _uriPrefix;
        private readonly IAccessServer _accessServer;

        public Uri BaseUri => new(_uriPrefix, "/api/");
        public IPEndPoint? RedirectServerEndPoint { get; set; }

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
                var json = await HttpContext.GetRequestBodyAsByteArrayAsync();
                var res = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (res == null)
                    throw new Exception($"The request expected to have a {typeof(T).Name} but it is null!");
                return res;
            }

            [Route(HttpVerbs.Get, "/")] //todo change to post
            public async Task<Access> Get([QueryField] string serverId, [QueryField] Guid tokenId, [QueryField] string requestEndPoint,
                [QueryField] Guid clientId, [QueryField] string? clientIp = null , [QueryField] string? clientVersion = null,
                [QueryField] string? userAgent = null, [QueryField] string? userToken = null)
            {
                _ = serverId;
                var accessRequest = new AccessRequest
                {
                    ClientInfo = new()
                    {
                        ClientId = clientId,
                        ClientIp = !string.IsNullOrEmpty(clientIp) ? IPAddress.Parse(clientIp) : null,
                        ClientVersion = clientVersion,
                        UserAgent = userAgent,
                        UserToken = userToken
                    },
                    TokenId = tokenId,
                    RequestEndPoint = IPEndPoint.Parse(requestEndPoint)

                };
                var res = await AccessServer.GetAccess(accessRequest);
                if (_embedIoAccessServer.RedirectServerEndPoint != null && !accessRequest.RequestEndPoint.Equals(_embedIoAccessServer.RedirectServerEndPoint))
                {
                    res.RedirectServerEndPoint = _embedIoAccessServer.RedirectServerEndPoint;
                    res.StatusCode = AccessStatusCode.RedirectServer;
                }
                return res;
            }

            [Route(HttpVerbs.Post, "/usage")]
            public async Task<Access> AddUsage([QueryField] Guid serverId, [QueryField] string accessId)
            {
                _ = serverId;
                var usageInfo = await GetRequestDataAsync<UsageInfo>();
                return await AccessServer.AddUsage(accessId, usageInfo);
            }

            [Route(HttpVerbs.Get, "/ssl-certificates/{requestEndPoint}")]
            public Task<byte[]> GetSslCertificateData([QueryField] string serverId, string requestEndPoint)
            {
                _ = serverId;
                return AccessServer.GetSslCertificateData(requestEndPoint);
            }

            [Route(HttpVerbs.Post, "/server-status")]
            public async Task SendServerStatus([QueryField] Guid serverId)
            {
                _ = serverId;
                var serverStatus = await GetRequestDataAsync<ServerStatus>();
                await AccessServer.SendServerStatus(serverStatus);
            }


            [Route(HttpVerbs.Post, "/server-subscribe")]
            public async Task ServerSubscribe([QueryField] string serverId)
            {
                _ = serverId;
                var serverInfo = await GetRequestDataAsync<ServerInfo>();
                await AccessServer.SubscribeServer(serverInfo);
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


