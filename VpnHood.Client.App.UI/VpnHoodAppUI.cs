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
        private string _indexHtml;
        private static VpnHoodAppUI _current;
        private WebServer _server;
        private readonly Stream _spaZipStream;

        public int DefaultPort { get; }
        public string Url { get; private set; }
        public string SpaHash { get; private set; }

        public static VpnHoodAppUI Current => _current ?? throw new InvalidOperationException($"{nameof(VpnHoodAppUI)} has not been initialized yet!");
        public static bool IsInit => _current != null;
        public static VpnHoodAppUI Init(Stream zipStream, int defaultPort = 9090)
        {
            var ret = new VpnHoodAppUI(zipStream, defaultPort);
            ret.Start();
            return ret;
        }

        public bool Started => _server != null;

        private class FilterModule : WebModuleBase
        {
            public FilterModule(string baseRoute) : base(baseRoute) { }
            public override bool IsFinalHandler => false;

            protected override Task OnRequestAsync(IHttpContext context)
            {
                return Task.FromResult(0);
            }
        }

        public VpnHoodAppUI(Stream spaZipStream, int defaultPort = 0)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            _spaZipStream = spaZipStream;
            DefaultPort = defaultPort;
            _current = this;
        }

        public Task Start()
        {
            if (!VpnHoodApp.IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized!");
            if (Started) throw new InvalidOperationException($"{nameof(VpnHoodAppUI)} has been already started!");

            Url = $"http://{Util.GetFreeEndPoint(IPAddress.Loopback, DefaultPort)}";
            _server = CreateWebServer(Url, GetSpaPath());
            return _server.RunAsync();
        }

        public void Stop()
        {
            _server?.Dispose();
            _server = null;
        }

        private string GetSpaPath()
        {
            using  var memZipStream = new MemoryStream();
            _spaZipStream.CopyTo(memZipStream);

            // extract the resource
            memZipStream.Seek(0, SeekOrigin.Begin);
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(memZipStream);
            SpaHash = BitConverter.ToString(hash).Replace("-", "");

            var path = Path.Combine(VpnHoodApp.Current.AppDataFolderPath, "SPA", SpaHash);
            if (!Directory.Exists(path) )
            {
                memZipStream.Seek(0, SeekOrigin.Begin);
                var zipArchive = new ZipArchive(memZipStream);
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
                    .WithMode(HttpListenerMode.EmbedIO));

            server
                .WithCors("https://localhost:8080, http://localhost:8080, https://localhost:8081, http://localhost:8081") // must be first
                 //.WithModule(new FilterModule("/"))
                .WithWebApi("/api", ResponseSerializerCallback, c => c.WithController<ApiController>())
                .WithStaticFolder("/", spaPath, true, c => c.HandleMappingFailed(HandleMappingFailed));

            return server;
        }


#nullable enable
        private async Task ResponseSerializerCallback(IHttpContext context, object? data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            context.Response.ContentType = MimeType.Json;
            using var text = context.OpenResponseText(new UTF8Encoding(false));
            await text.WriteAsync(JsonSerializer.Serialize(data, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
#nullable disable

        // manage SPA fallback
        private Task HandleMappingFailed(IHttpContext context, MappedResourceInfo info)
        {
            if (string.IsNullOrEmpty(Path.GetExtension(context.Request.Url.LocalPath)))
                return context.SendStringAsync(_indexHtml, "text/html", Encoding.UTF8);
            throw HttpException.NotFound();
        }

        public void Dispose()
        {
            Stop();
            if (_current == this)
                _current = null;
        }
    }
}
