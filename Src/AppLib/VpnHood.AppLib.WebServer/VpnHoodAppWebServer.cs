using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer.Controllers;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.WebServer;

public class VpnHoodAppWebServer : Singleton<VpnHoodAppWebServer>, IDisposable
{
    private string? _indexHtml;
    private WebserverLite? _server;
    private string? _spaHash;
    private string? _spaPath;
    private readonly bool _isDebugMode;
    public Uri Url { get; }

    public string SpaHash => _spaHash ?? throw new InvalidOperationException($"{nameof(SpaHash)} is not initialized");
    public bool UseHostName { get; set; }

    private VpnHoodAppWebServer(WebServerOptions options)
    {
        _isDebugMode = VpnHoodApp.Instance.Features.IsDebugMode;
        var defaultPort = VpnHoodApp.Instance.Features.WebUiPort ?? 9090;
        var host = IPAddress.Loopback; // fallback safe default; adjust if you have AllowRemoteAccess
        var endPoint = VhUtils.GetFreeTcpEndPoint(host, defaultPort);
        Url = options.Url ?? new Uri($"http://{endPoint}");
        VpnHoodApp.Instance.SettingsService.BeforeSave += SettingsServiceOnBeforeSave;
    }

    private void SettingsServiceOnBeforeSave(object? sender, EventArgs e)
    {
        if (VpnHoodApp.Instance.SettingsService.OldUserSettings.AllowRemoteAccess !=
            VpnHoodApp.Instance.SettingsService.UserSettings.AllowRemoteAccess)
            Restart();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Stop();

        base.Dispose(disposing);
    }

    public static VpnHoodAppWebServer Init(WebServerOptions? options = null)
    {
        var ret = new VpnHoodAppWebServer(options ?? new WebServerOptions());
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
         oldServer.TryStop();
         oldServer.Dispose();
        _server = null;
    }

    public void Restart()
    {
        Stop();
        Start();
    }

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

        var host = VpnHoodApp.Instance.UserSettings.AllowRemoteAccess ? IPAddress.Any.ToString() : Url.Host;
        var settings = new WebserverSettings(host, Url.Port);
        var server = new WebserverLite(settings, ctx => DefaultRoute(ctx, spaPath));

        // Initialize API routes through controllers - CORS is handled centrally in the route mapper
        server
            .AddRouteMapper(_isDebugMode)
            .AddController(new AppController())
            .AddController(new ClientProfileController())
            .AddController(new AccountController())
            .AddController(new BillingController())
            .AddController(new IntentsController())
            .AddController(new ProxyEndPointController());

        return server;
    }

    private static Task ServeFile(HttpContextBase context, string fullPath)
    {
        var contentType = MimeTypeHelper.GetContentType(fullPath);
        context.Response.ContentType = contentType;
        return context.Response.Send(File.ReadAllBytes(fullPath));
    }

    private async Task DefaultRoute(HttpContextBase context, string spaPath)
    {
        if (_indexHtml == null)
            throw new InvalidOperationException($"{nameof(_indexHtml)} is not initialized");

        // Add CORS centrally for default route
        CorsMiddleware.AddCors(context, _isDebugMode);

        if (context.Request.Url.RawWithoutQuery.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.Send();
            return;
        }

        // use LocalPath for security reasons (Url.PathAndQuery can contain double dots)
        var localPath = context.Request.Url.Uri.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(spaPath, localPath);
        if (File.Exists(fullPath)) {
            await ServeFile(context, fullPath);
            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.Send(_indexHtml);
    }

    // ReSharper disable once UnusedMember.Local
    private static IEnumerable<IPAddress> GetAllPublicIp4()
    {
        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus is OperationalStatus.Up && x.Supports(NetworkInterfaceComponent.IPv4) &
                x.NetworkInterfaceType is not NetworkInterfaceType.Loopback);

        var ipAddresses = new List<IPAddress>();
        foreach (var networkInterface in networkInterfaces) {
            var ipProperties = networkInterface.GetIPProperties();
            var uniCastAddresses = ipProperties.UnicastAddresses;
            var ips = uniCastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.Address);
            ipAddresses.AddRange(ips);
        }

        return ipAddresses.ToArray();
    }
}