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

namespace VpnHood.Client.App.UI
{

    public class VpnHoodAppUI : IDisposable
    {
        private string _indexHtml;
        private static VpnHoodAppUI _current;
        private WebServer _server;

        public int DefaultPort { get; }
        public string Url { get; private set; }
        public string SpaHash { get; private set; }

        public static VpnHoodAppUI Current => _current ?? throw new InvalidOperationException($"{nameof(VpnHoodAppUI)} has not been initialized yet!");
        public static bool IsInit => _current != null;
        public static VpnHoodAppUI Init(int defaultPort = 9898) => new VpnHoodAppUI(defaultPort);
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

        public VpnHoodAppUI(int defaultPort = 9898)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            DefaultPort = defaultPort;
            _current = this;
        }

        public Task Start()
        {
            if (!VpnHoodApp.IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized!");
            if (Started) throw new InvalidOperationException($"{nameof(VpnHoodAppUI)} has been already started!");

            Url = $"http://127.0.0.1:{GetFreePort()}";
            _server = CreateWebServer(Url);
            return _server.RunAsync();
        }

        public int GetFreePort()
        {
            try
            {
                // check recommended port
                var listener = new TcpListener(IPAddress.Loopback, 9898);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
            catch
            {
                // try any port
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }

        public void Stop()
        {
            _server?.Dispose();
            _server = null;
        }

        private string GetSpaPath()
        {
            // extract the resource
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(AppUIResource.HtmlArchive);
            SpaHash = BitConverter.ToString(hash).Replace("-", "");

            var path = Path.Combine(VpnHoodApp.Current.AppDataFolderPath, "SPA", SpaHash);
            if (!Directory.Exists(path))
            {
                var zipArchive = new ZipArchive(new MemoryStream(AppUIResource.HtmlArchive));
                zipArchive.ExtractToDirectory(path, true);
            }
            return path;
        }

        private WebServer CreateWebServer(string url)
        {
            // read index.html for fallback
            var zipStream = new MemoryStream(AppUIResource.HtmlArchive);
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, true))
            using (var streamReader = new StreamReader(archive.GetEntry("index.html").Open()))
                _indexHtml = streamReader.ReadToEnd();
            zipStream.Seek(0, SeekOrigin.Begin);

            // create the server
            var server = new WebServer(o => o
                    .WithUrlPrefix(url)
                    .WithMode(HttpListenerMode.EmbedIO));

            server
                .WithCors("https://localhost:8080, http://localhost:8080") // must be first
                //.WithModule(new FilterModule("/"))
                .WithWebApi("/api", ResponseSerializerCallback, c => c.WithController<ApiController>())
                .WithStaticFolder("/", GetSpaPath(), true, c => c.HandleMappingFailed(HandleZipMappingFailed))
                ;

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
        private Task HandleZipMappingFailed(IHttpContext context, MappedResourceInfo info)
        {
            if (string.IsNullOrEmpty(Path.GetExtension(context.Request.Url.LocalPath)))
                return context.SendStringAsync(_indexHtml, "text/html", Encoding.UTF8);
            return Task.FromResult(0);
        }

        public void Dispose()
        {
            Stop();
            if (_current == this)
                _current = null;
        }
    }
}
