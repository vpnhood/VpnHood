using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The app's state, asked each second over the window's API for what shows it outside the window - the tray, the
// taskbar button - which read it once between them, the service's app and the one in this process alike. A state
// that cannot be read - a service restarting - is asked again. Followed from the UI thread's loop on, where its
// awaits come back to, so StateRead is raised on the UI thread.
internal sealed class AvaloniaAppStateFollower : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly VpnHoodApi _api;
    private readonly CancellationTokenSource _cancellation = new();

    public event EventHandler<AppState>? StateRead;

    public AvaloniaAppStateFollower(VpnHoodApi api)
    {
        _api = api;
        var cancellationToken = _cancellation.Token;
        Dispatcher.UIThread.Post(() => _ = Follow(cancellationToken));
    }

    private async Task Follow(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var state = await TryReadState(cancellationToken);
            if (state != null && !cancellationToken.IsCancellationRequested)
                StateRead?.Invoke(this, state);

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing |
                                                                             ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    // null where it cannot be read, or the follow ends
    private async Task<AppState?> TryReadState(CancellationToken cancellationToken)
    {
        try {
            return await _api.App.GetState(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not read the app's state for the tray and the taskbar.");
            return null;
        }
    }

    public void Dispose()
    {
        if (_cancellation.IsCancellationRequested)
            return;

        _cancellation.TryCancel();
        _cancellation.Dispose();
    }
}
