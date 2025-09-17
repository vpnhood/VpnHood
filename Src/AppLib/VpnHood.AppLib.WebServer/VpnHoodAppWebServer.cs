using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer;

public class VpnHoodAppWebServer : Singleton<VpnHoodAppWebServer>, IDisposable
{
    private string? _indexHtml;
    private WebserverLite? _server;
    private string? _spaHash;

    private VpnHoodAppWebServer(WebServerOptions options)
    {
        var defaultPort = options.DefaultPort ?? 9090;
        var host = IPAddress.Loopback; // fallback safe default; adjust if you have AllowRemoteAccess
        var endPoint = VhUtils.GetFreeTcpEndPoint(host, defaultPort);
        Url = options.Url ?? new Uri($"http://{endPoint}");

        VpnHoodApp.Instance.SettingsService.BeforeSave += SettingsServiceOnBeforeSave;
    }

    private void SettingsServiceOnBeforeSave(object? sender, EventArgs e)
    {
        Restart();
    }

    public Uri Url { get; }

    public string SpaHash =>
        _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Stop();

        base.Dispose(disposing);
    }

    public static VpnHoodAppWebServer Init(WebServerOptions options)
    {
        var ret = new VpnHoodAppWebServer(options);
        ret.Start();
        return ret;
    }

    public void Start()
    {
        VhLogger.Instance.LogInformation("Starting web server...");

        if (_server != null) {
            _server.Start();
            return;
        }

        _server = CreateWebServer();
        _server.Start();
        VhLogger.Instance.LogInformation("Web server has been started on {Url}", Url);
    }

    public void Stop()
    {
        var oldServer = _server;
        if (oldServer == null)
            return;

        VhLogger.Instance.LogInformation("Stopping web server...");
        oldServer.Stop();
        _server = null;

        // give some time for old server to dispose to prevent crash if there are active connections
        _ = Task.Delay(5000).ContinueWith(_ => oldServer.Dispose());
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private string? _spaPath;

    private string GetSpaPath()
    {
        if (_spaPath != null)
            return _spaPath; // do not extract in same instance

        if (VpnHoodApp.Instance.Resources.SpaZipData is null)
            throw new InvalidOperationException("SpaZipData resource is required to run web server for SPA.");

        using var memZipStream = new MemoryStream(VpnHoodApp.Instance.Resources.SpaZipData);
        memZipStream.Seek(0, SeekOrigin.Begin);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(memZipStream);
        _spaHash = BitConverter.ToString(hash).Replace("-", "");

        var spaFolderPath = Path.Combine(VpnHoodApp.Instance.StorageFolderPath, "Temp", "SPA");
        var spaPath = Path.Combine(spaFolderPath, _spaHash);
        var htmlPath = Path.Combine(spaPath, "index.html");
        if (!File.Exists(htmlPath)) {
            if (Directory.Exists(spaFolderPath))
                VhUtils.TryInvoke("Delete old SPA folder", () => Directory.Delete(spaFolderPath, true));
            memZipStream.Seek(0, SeekOrigin.Begin);
            using var zipArchive = new ZipArchive(memZipStream);
            zipArchive.ExtractToDirectory(spaPath, true);
        }

        _spaPath = spaPath;
        return spaPath;
    }

    private WebserverLite CreateWebServer()
    {
        var spaPath = GetSpaPath();
        _indexHtml = File.ReadAllText(Path.Combine(spaPath, "index.html"));

        var settings = new WebserverSettings(Url.Host, Url.Port);
        var server = new WebserverLite(settings, ctx => DefaultRoute(ctx, spaPath));

        _ = new WatsonApiRouteMapper(server);

        server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", ctx => ServeFile(ctx, Path.Combine(spaPath, "index.html")));
        server.Routes.PreAuthentication.Parameter.Add(HttpMethod.GET, "/static/{path}", ctx => ServeStatic(ctx, spaPath));
        return server;
    }

    private Task ServeStatic(HttpContextBase context, string root)
    {
        AddCors(context);
        var rel = string.Join('/', context.Request.Url.Elements.Skip(1)); // skip leading 'static'
        var full = Path.Combine(root, rel);
        if (!File.Exists(full)) return NotFound(context);
        var contentType = MimeTypeHelper.GetContentType(full);
        context.Response.ContentType = contentType;
        return context.Response.Send(File.ReadAllBytes(full));
    }

    private Task ServeFile(HttpContextBase context, string fullPath)
    {
        AddCors(context);
        var contentType = MimeTypeHelper.GetContentType(fullPath);
        context.Response.ContentType = contentType;
        return context.Response.Send(File.ReadAllBytes(fullPath));
    }

    private async Task DefaultRoute(HttpContextBase context, string spaPath)
    {
        if (_indexHtml == null)
            throw new InvalidOperationException($"{nameof(_indexHtml)} is not initialized");

        AddCors(context);

        if (context.Request.Url.RawWithoutQuery.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.Send();
            return;
        }

        var localPath = context.Request.Url.RawWithoutQuery.TrimStart('/');
        if (Path.HasExtension(localPath)) {
            var full = Path.Combine(spaPath, localPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) {
                await ServeFile(context, full);
                return;
            }
        }

        context.Response.ContentType = "text/html";
        await context.Response.Send(_indexHtml);
    }

    private static void AddCors(HttpContextBase context)
    {
        var cors = VpnHoodApp.Instance.Features.IsDebugMode ? "*" : "https://localhost:8080, http://localhost:8080, https://localhost:8081, http://localhost:8081, http://localhost:30080";
        context.Response.Headers.Add("Access-Control-Allow-Origin", cors);
        context.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
    }

    private static Task NotFound(HttpContextBase context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        return context.Response.Send();
    }

    private static IEnumerable<IPAddress> GetAllPublicIp4()
    {
        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus is OperationalStatus.Up && x.Supports(NetworkInterfaceComponent.IPv4) & x.NetworkInterfaceType is not NetworkInterfaceType.Loopback);

        var ipAddresses = new List<IPAddress>();
        foreach (var networkInterface in networkInterfaces) {
            var ipProperties = networkInterface.GetIPProperties();
            var uniCastAddresses = ipProperties.UnicastAddresses;
            var ips = uniCastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).Select(x => x.Address);
            ipAddresses.AddRange(ips);
        }

        return ipAddresses.ToArray();
    }
}