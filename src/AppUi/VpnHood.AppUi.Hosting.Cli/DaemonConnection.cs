using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.AppLib.Api.UiAttachments;
using VpnHood.AppUi.Hosting.Cli.Internal;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli;

// The way in for everything that is not the daemon. The window and every command get the app's API
// here - the same six interfaces the daemon holds in process, over loopback instead. No token is
// presented and none is asked for: the web host treats a loopback caller as the device itself and
// never requires pairing (VpnHoodAppWebHost.OnPreRouting).
//
// Open waits, and that is the whole reason this is not a one-line factory. Starting the instance
// returns as soon as the platform has STARTED it, while the address a caller needs is published a
// few seconds later by the daemon itself, when its listener has actually bound. Without the wait,
// the first command after an install or a reboot fails on a service that is coming up perfectly -
// and that is exactly the moment somebody is typing the commands the installer just printed.
//
// An instance that is not running at all is not waited for: that is answered at once, with the
// platform's sentence on how to start it.
public sealed class DaemonConnection : IDisposable
{
    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly UiAttachmentHandler _uiAttachmentHandler = new();
    private readonly HttpClient _httpClient;

    internal DaemonConnection(Uri apiUrl)
    {
        Url = apiUrl;
        _httpClient = new HttpClient(_uiAttachmentHandler) {
            BaseAddress = new Uri(apiUrl.GetLeftPart(UriPartial.Authority) + "/"),
            Timeout = TimeSpan.FromMinutes(2) // a connect attempt walks a server list and may be slow
        };

        Api = VpnHoodApiHttpFactory.Create(_httpClient);
        UiAttachments = VpnHoodApiHttpFactory.CreateUiAttachments(_httpClient);
    }

    public VpnHoodApi Api { get; }

    // The window's attachment to the daemon (DaemonUiAttachment); the commands never attach.
    internal IUiAttachmentsApi UiAttachments { get; }

    // Every request from here on names this attachment - or none, once the window has detached.
    internal string? UiAttachmentId {
        get => _uiAttachmentHandler.AttachmentId;
        set => _uiAttachmentHandler.AttachmentId = value;
    }

    // The address the daemon published: its local web host, which serves the page and the API.
    public Uri Url { get; }

    public static async Task<DaemonConnection> Open(CliPlatform platform, CancellationToken cancellationToken)
    {
        var daemonInfo = DaemonInfo.Read(platform.Paths.DaemonInfoFilePath) ??
                         await WaitForDaemon(platform, cancellationToken).Vhc();

        return new DaemonConnection(daemonInfo.ApiUrl);
    }

    private static async Task<DaemonInfo> WaitForDaemon(CliPlatform platform, CancellationToken cancellationToken)
    {
        // Nothing is starting, so there is nothing to wait for.
        if (!await platform.Instance.IsRunning(cancellationToken).Vhc())
            throw new DaemonNotRunningException(platform.Instance.NotRunningHint);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BindTimeout);

        try {
            while (true) {
                await Task.Delay(PollInterval, timeout.Token).Vhc();
                var daemonInfo = DaemonInfo.Read(platform.Paths.DaemonInfoFilePath);
                if (daemonInfo != null)
                    return daemonInfo;

                // It died while we waited; say that rather than spend the whole timeout on it.
                if (!await platform.Instance.IsRunning(cancellationToken).Vhc())
                    throw new DaemonNotRunningException(platform.Instance.NotRunningHint);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new InvalidOperationException(
                $"{platform.Paths.InstanceName} is running but has not answered. " +
                $"See: {platform.Paths.CommandName} service log");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
