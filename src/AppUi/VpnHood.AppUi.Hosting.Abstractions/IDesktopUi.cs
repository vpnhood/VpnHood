namespace VpnHood.AppUi.Hosting.Abstractions;

// A UI a desktop head names - Avalonia's window, a web view, a fork's own - which the head's host
// runs. The host decides where the app is, and so what it hands over (DesktopUiParams); the UI
// neither knows nor asks, which is what lets one UI serve the app in a service and the app in its
// own process alike. A UI publishes its window as AppUiContext.Context: that is how what the app
// asks of a UI - opening a link, coming to the front - reaches it, wherever the app runs.
public interface IDesktopUi
{
    // Runs the UI on the calling thread - the host's main thread, which on Windows must be its STA
    // thread - and returns when the UI's window is gone. Cancelling asks the UI to close it: the
    // host's own end, a service manager's signal or a logout, and the process cannot end while
    // this holds its main thread.
    void Run(DesktopUiParams uiParams, CancellationToken cancellationToken);
}
