using System.Net;
using VpnHood.AppLib.App.WebHosting;

namespace VpnHood.AppUi.Hosting.WebView;

// The app's local web host as a UI in another process sees it: an address, served and kept alive by
// the process that runs the app - the service, on a desktop. There is nothing here to start, stop or
// rebind; a load that fails is retried by WebViewHost against the same address, which the service
// keeps for its whole run.
public sealed class ExternalAppWebHost(Uri url) : IAppWebHost
{
    public Task<Uri> EnsureStarted(CancellationToken cancellationToken) => Task.FromResult(url);
    public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;

    // the service rebinds its own listener; a page loaded from it reloads on a failed load instead
    public event EventHandler? Restarted {
        add { }
        remove { }
    }

    public bool IsActive => true;
    public bool IsAlwaysOn => true;
    public IReadOnlyList<Uri> Urls => [url];
    public IReadOnlyList<IPAddress> ConnectedDevices => [];

    public void Dispose()
    {
    }
}
