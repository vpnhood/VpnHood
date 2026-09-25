using System.Windows;
using VpnHood.AppLib.App.Windows;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The WPF window as the UI's context. Its actions run in the window's process, the person's own, so a
// link opens in their browser.
public class WpfUiContext(VpnHoodWpfMainWindow window) : IUiContext
{
    public async Task<bool> IsActive()
    {
        try {
            return await window.Dispatcher.InvokeAsync(() => window.IsActive);
        }
        catch {
            return false; // If the window is destroyed, we assume it's not active
        }
    }

    // could not find a way to check if the window is destroyed in WPF,
    // so we assume it's not destroyed if it's active
    public async Task<bool> IsDestroyed()
    {
        try {
            return await window.Dispatcher.InvokeAsync(() => !window.IsActive);
        }
        catch {
            return false; // If the window is destroyed, we assume it's destroyed
        }
    }

    // The person's default browser, through the shell.
    public Task OpenUrl(Uri url, CancellationToken cancellationToken)
    {
        WindowsShell.OpenUrl(url);
        return Task.CompletedTask;
    }

    public async Task BringToFront(CancellationToken cancellationToken)
    {
        await window.Dispatcher.InvokeAsync(window.ShowOrOpen);
    }
}
