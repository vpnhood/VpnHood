namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// The window's thread as an await sees it, as WPF's dispatcher made it: what follows an await there
// runs in the window's message loop again. WebView2's objects belong to that thread, and its
// callbacks come through that loop.
internal sealed class Win32SynchronizationContext(Win32Window window) : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state)
    {
        window.Post(() => callback(state));
    }

    // From another thread it would wait on a loop that may already have ended.
    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (!window.IsOwnThread)
            throw new NotSupportedException("Send is supported only on the window's own thread.");

        callback(state);
    }

    public override SynchronizationContext CreateCopy()
    {
        return this;
    }
}
