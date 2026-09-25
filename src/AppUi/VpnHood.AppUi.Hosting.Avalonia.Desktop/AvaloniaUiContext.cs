using Avalonia.Controls;
using Avalonia.Threading;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The window as the app's UI context: active while it is the foreground window, gone while it is
// hidden - there is nothing to show on then, which is what a caller is asking about. Its actions run
// in the window's process, so a link opens in the browser of the person who sees the window.
internal sealed class AvaloniaUiContext(Window window) : IUiContext
{
    public Task<bool> IsActive()
    {
        return Dispatcher.UIThread.InvokeAsync(() => window.IsActive).GetTask();
    }

    public Task<bool> IsDestroyed()
    {
        return Dispatcher.UIThread.InvokeAsync(() => !window.IsVisible).GetTask();
    }

    // The browser the platform opens a link in (Avalonia's launcher: the shell on Windows,
    // xdg-open or the desktop portal on Linux).
    public async Task OpenUrl(Uri url, CancellationToken cancellationToken)
    {
        var isLaunched = await Dispatcher.UIThread.InvokeAsync(() => window.Launcher.LaunchUriAsync(url));
        if (!isLaunched)
            throw new InvalidOperationException($"No browser opened {url}.");
    }

    public Task BringToFront(CancellationToken cancellationToken)
    {
        return Dispatcher.UIThread.InvokeAsync(() => {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }).GetTask();
    }
}
