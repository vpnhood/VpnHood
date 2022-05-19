using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Files;
using EmbedIO.WebApi;
using Swan.Logging;
using VpnHood.Common;

namespace VpnHood.Client.App.UI
{
    public class VpnHoodAppUi : IDisposable
    {
        private static VpnHoodAppUi? _instance;
        private readonly Stream _spaZipStream;
        private string? _indexHtml;
        private WebServer? _server;
        private string? _spaHash;
        private string? _url;

        private VpnHoodAppUi(Stream spaZipStream, int defaultPort = 0)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            _spaZipStream = spaZipStream;
            DefaultPort = defaultPort;
            _instance = this;
        }

        public int DefaultPort { get; }
        public string Url => _url ?? throw new InvalidOperationException($"{nameof(Url)} is not initialized");

        public string SpaHash =>
            _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");

        public static VpnHoodAppUi Instance => _instance ??
                                               throw new InvalidOperationException(
                                                   $"{nameof(VpnHoodAppUi)} has not been initialized yet!");

        public static bool IsInit => _instance != null;

        public void Dispose()
        {
            Stop();
            if (_instance == this)
                _instance = null;
        }

        public static VpnHoodAppUi Init(Stream zipStream, int defaultPort = 9090)
        {
            var ret = new VpnHoodAppUi(zipStream, defaultPort);
            ret.Start();
            return ret;
        }

        private void Start()
        {
            _url = $"http://{Util.GetFreeEndPoint(IPAddress.Loopback, DefaultPort)}";
            _server = CreateWebServer(Url, GetSpaPath());
            try
            {
                Logger.UnregisterLogger<ConsoleLogger>();
            }
            catch
            {
                // ignored
            }

            _server.RunAsync();
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
                try
                {
                    Directory.Delete(spaFolderPath, true);
                }
                catch
                {
                    // ignored
                }

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
                .WithCors(
                    "https://localhost:8080, http://localhost:8080, https://localhost:8081, http://localhost:8081, http://localhost:30080") // must be first
                .WithWebApi("/api", ResponseSerializerCallback, c => c.WithController<ApiController>())
                .WithStaticFolder("/", spaPath, true, c => c.HandleMappingFailed(HandleMappingFailed));

            return server;
        }


        private async Task ResponseSerializerCallback(IHttpContext context, object? data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            context.Response.ContentType = MimeType.Json;
            await using var text = context.OpenResponseText(new UTF8Encoding(false));
            await text.WriteAsync(JsonSerializer.Serialize(data,
                new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase}));
        }

        // manage SPA fallback
        private Task HandleMappingFailed(IHttpContext context, MappedResourceInfo? info)
        {
            if (_indexHtml == null) throw new InvalidOperationException($"{nameof(_indexHtml)} is not initialized");

            if (string.IsNullOrEmpty(Path.GetExtension(context.Request.Url.LocalPath)))
                return context.SendStringAsync(_indexHtml, "text/html", Encoding.UTF8);
            throw HttpException.NotFound();
        }
    }
}