namespace VpnHood.AppFramework.WebServer;

public class WebServerOptions
{
    public required Stream SpaZipStream { get; init; }
    public int? DefaultPort { get; init; }
    public Uri? Url { get; init; }
    public bool ListenOnAllIps { get; init; }
}