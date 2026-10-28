using Avalonia.Controls;
using Avalonia.Threading;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The window as the app's UI context: active while it is the foreground window, gone while it is
// hidden - there is nothing to show on then, which is what a caller is asking about.
internal sealed class AvaloniaUiContext(Window window) : IWindowHandleUiContext
{
    public Task<bool> IsActive()
    {
        return Dispatcher.UIThread.InvokeAsync(() => window.IsActive).GetTask();
    }

    public Task<bool> IsDestroyed()
    {
        return Dispatcher.UIThread.InvokeAsync(() => !window.IsVisible).GetTask();
    }

    public Task<IntPtr> GetWindowHandle()
    {
        return Dispatcher.UIThread.InvokeAsync(() => window.TryGetPlatformHandle()?.Handle ??
            throw new InvalidOperationException("The window has no native handle.")).GetTask();
    }

    public Task<T> RunOnUiThread<T>(Func<T> action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
