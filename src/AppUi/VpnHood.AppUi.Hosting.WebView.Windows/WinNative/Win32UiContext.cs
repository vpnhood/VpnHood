using System.Runtime.InteropServices;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// The window as the app's UI context, as the Avalonia window is: active while it is the foreground
// window, gone while it is hidden - there is nothing to show on then, which is what a caller is
// asking about. Windows answers both on any thread.
internal sealed class Win32UiContext(Win32Window window) : IWindowHandleUiContext
{
    public Task<bool> IsActive()
    {
        return Task.FromResult(GetForegroundWindow() == window.Handle);
    }

    public Task<bool> IsDestroyed()
    {
        return Task.FromResult(!window.IsVisible);
    }

    public Task<IntPtr> GetWindowHandle()
    {
        return Task.FromResult(window.Handle);
    }

    public Task<T> RunOnUiThread<T>(Func<T> action)
    {
        // completed on the window's thread; the awaiting caller must not resume inline on it
        var taskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Post(() => {
            try {
                taskCompletionSource.TrySetResult(action());
            }
            catch (Exception ex) {
                taskCompletionSource.TrySetException(ex);
            }
        });

        return taskCompletionSource.Task;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
