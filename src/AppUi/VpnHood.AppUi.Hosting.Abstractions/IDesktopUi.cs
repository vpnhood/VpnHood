namespace VpnHood.AppUi.Hosting.Abstractions;

// A UI a desktop head names - Avalonia's window, a web view, a fork's own - which the head's host
// runs. The host decides where the app is, and so what it hands over (DesktopUiParams); the UI
// neither knows nor asks, which is what lets one UI serve the app in a service and the app in its
// own process alike.
public interface IDesktopUi
{
    // Runs the UI on the calling thread - the host's main thread, which on Windows must be its STA
    // thread - and returns when the UI's window is gone. Cancelling asks the UI to close it: the
    // host's own end, a service manager's signal or a logout, and the process cannot end while
    // this holds its main thread.
    void Run(DesktopUiParams uiParams, CancellationToken cancellationToken);

    // In place of Run when the host has no app to hand over: one message and a Close button, on the
    // calling thread as Run is, until the person closes it or the host cancels. Nothing here calls
    // the app. BringToFront reaches this window too.
    void RunMessage(DesktopUiMessageParams messageParams, CancellationToken cancellationToken);

    // Shows the running UI's window and brings it to the front, from any thread: the tray's Open, a
    // second launch. Nothing while no run has a window.
    Task BringToFront(CancellationToken cancellationToken);
}
