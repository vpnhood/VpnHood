using EmbedIO;
using EmbedIO.Files;
using EmbedIO.WebApi;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.Common;

namespace VpnHood.Client.App.UI
{

    public class VpnHoodAppUI : IDisposable
    {
        private static VpnHoodAppUI? _instance;
        private string? _indexHtml;
        private WebServer? _server;
        private string? _url;
        private string? _spaHash;
        private readonly Stream _spaZipStream;

        public int DefaultPort { get; }
        public string Url => _url ?? throw new InvalidOperationException($"{nameof(Url)} is not initialized");
        public string SpaHash => _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");
        public static VpnHoodAppUI Instance => _instance ?? throw new InvalidOperationException($"{nameof(VpnHoodAppUI)} has not been initialized yet!");
        public static bool IsInit => _instance != null;

        public static VpnHoodAppUI Init(Stream zipStream, int defaultPort = 9090)
        {
            var ret = new VpnHoodAppUI(zipStream, defaultPort);
            ret.Start();
            return ret;
        }

        private class FilterModule : WebModuleBase
        {
            public FilterModule(string baseRoute) : base(baseRoute) { }
            public override bool IsFinalHandler => false;

            protected override Task OnRequestAsync(IHttpContext context)
            {
                return Task.FromResult(0);
            }
        }

        private VpnHoodAppUI(Stream spaZipStream, int defaultPort = 0)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            _spaZipStream = spaZipStream;
            DefaultPort = defaultPort;
            _instance = this;
        }

        private Task Start()
        {
            _url = $"http://{Util.GetFreeEndPoint(IPAddress.Loopback, DefaultPort)}";
            _server = CreateWebServer(Url, GetSpaPath());
            try { Swan.Logging.Logger.UnregisterLogger<Swan.Logging.ConsoleLogger>(); } catch { }
            return _server.RunAsync();
        }

        public void Stop()
        {
            _server?.Dispose();
        }

        private string GetSpaPath()
        {
            using var memZipStream = new MemoryStream();
            _spaZipStream.CopyTo(memZipStream);

            // extract the resource
            memZipStream.Seek(0, SeekOrigin.Begin);
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(memZipStream);
            _spaHash = BitConverter.ToString(hash).Replace("-", "");

            var spaFolderPath = Path.Combine(VpnHoodApp.Instance.AppDataFolderPath, "Temp", "SPA");
            var path = Path.Combine(spaFolderPath, _spaHash);
            if (!Directory.Exists(path))
            {
                try { Directory.Delete(spaFolderPath, true); } catch { };
                memZipStream.Seek(0, SeekOrigin.Begin);
                using var zipArchive = new ZipArchive(memZipStream);
                zipArchive.ExtractToDirectory(path, true);
            }

            _spaZipStream.Dispose();
            return path;
        }

        private WebServer CreateWebServer(string url, string spaPath)
        {
            // read index.html for fallback
            _indexHtml = File.ReadAllText(Path.Combine(spaPath, "index.html"));

            // create the server
            var server = new WebServer(o => o
                    .WithUrlPrefix(url)
                    .WithMode(HttpListenerMode.EmbedIO))
                    .WithCors("https://localhost:8080, http://localhost:8080, https://localhost:8081, http://localhost:8081") // must be first
                    .WithWebApi("/api", ResponseSerializerCallback, c => c.WithController<ApiController>())
                    .WithStaticFolder("/", spaPath, true, c => c.HandleMappingFailed(HandleMappingFailed));

            return server;
        }


        private async Task ResponseSerializerCallback(IHttpContext context, object? data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            context.Response.ContentType = MimeType.Json;
            using var text = context.OpenResponseText(new UTF8Encoding(false));
            await text.WriteAsync(JsonSerializer.Serialize(data, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        // manage SPA fallback
        private Task HandleMappingFailed(IHttpContext context, MappedResourceInfo? info)
        {
            if (_indexHtml == null) throw new InvalidOperationException($"{nameof(_indexHtml)} is not initialized");

            if (string.IsNullOrEmpty(Path.GetExtension(context.Request.Url.LocalPath)))
                return context.SendStringAsync(_indexHtml, "text/html", Encoding.UTF8);
            throw HttpException.NotFound();
        }

        public void Dispose()
        {
            Stop();
            if (_instance == this)
                _instance = null;
        }
    }
}
