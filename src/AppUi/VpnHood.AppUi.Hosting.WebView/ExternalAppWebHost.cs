using System.Net;
using VpnHood.AppLib.App.WebHosting;
using VpnHood.AppUi.Hosting.Abstractions;

namespace VpnHood.AppUi.Hosting.WebView;

// The app's local web host as a UI in another process sees it: an API URL the service serves and
// moves. A new URL is a Restarted, on which WebViewHost loads the page again.
public sealed class ExternalAppWebHost : IAppWebHost
{
    private readonly IDesktopApiUrlProvider _apiUrlProvider;

    public ExternalAppWebHost(IDesktopApiUrlProvider apiUrlProvider)
    {
        _apiUrlProvider = apiUrlProvider;
        _apiUrlProvider.Changed += OnApiUrlChanged;
    }

    public Task<Uri> EnsureStarted(CancellationToken cancellationToken) => Task.FromResult(_apiUrlProvider.Current);
    public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;

    public event EventHandler? Restarted;

    public bool IsActive => true;
    public bool IsAlwaysOn => true;
    public IReadOnlyList<Uri> Urls => [_apiUrlProvider.Current];
    public IReadOnlyList<IPAddress> ConnectedDevices => [];

    private void OnApiUrlChanged(object? sender, EventArgs e)
    {
        Restarted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _apiUrlProvider.Changed -= OnApiUrlChanged;
    }
}
