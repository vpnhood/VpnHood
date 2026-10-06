using System.Runtime.InteropServices;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// The window as the app's UI context, as the Avalonia window is: active while it is the foreground
// window, gone while it is hidden - there is nothing to show on then, which is what a caller is
// asking about. Windows answers both on any thread.
internal sealed class Win32UiContext(Win32Window window) : IUiContext
{
    public Task<bool> IsActive()
    {
        return Task.FromResult(GetForegroundWindow() == window.Handle);
    }

    public Task<bool> IsDestroyed()
    {
        return Task.FromResult(!window.IsVisible);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
