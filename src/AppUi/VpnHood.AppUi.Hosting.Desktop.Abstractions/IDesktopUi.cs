namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>
/// Defines a UI a desktop head names, as Avalonia's window, a web view or a fork's own, which the head's host runs.
/// </summary>
/// <remarks>
/// The host decides where the app is, and so what it hands over (<see cref="DesktopUiParams"/>); the UI neither knows
/// nor asks, so one UI serves the app in a service and the app in its own process alike.
/// </remarks>
public interface IDesktopUi
{
    /// <summary>
    /// Runs the UI on the calling thread, the host's main thread, which on Windows must be its STA thread, and returns when
    /// the UI ends: its window closed, or its tray's Exit where it keeps a tray.
    /// </summary>
    /// <param name="uiParams">What the UI runs against.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests: a cancel asks the UI to end, as at the host's own end, a service
    /// manager's signal or a sign-out.
    /// </param>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused the UI's window, for a UI that makes a Win32 window.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled while the UI started.</exception>
    /// <exception cref="InvalidOperationException">The UI cannot start, as where it is given no content store.</exception>
    void Run(DesktopUiParams uiParams, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one message and a button that closes it, on the calling thread, in place of <see cref="Run"/> where the host
    /// has no app to hand over, until the person closes it or the host cancels.
    /// </summary>
    /// <param name="messageParams">The message.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests: a cancel ends the window.</param>
    /// <remarks>Nothing here calls the app. <see cref="BringToFront"/> reaches this window too.</remarks>
    void RunMessage(DesktopUiMessageParams messageParams, CancellationToken cancellationToken);

    /// <summary>Asks for the running UI's window, or its message, to show in front, as for a second launch.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// It may be called from any thread. Before a run has a window, a UI may keep the request for the run's start, as
    /// Avalonia's does, or drop it. The task may complete once the request is queued, before the window shows, and the
    /// token may not be read; a web UI without its web view may open the page in the browser instead.
    /// </remarks>
    Task BringToFront(CancellationToken cancellationToken);

    /// <summary>
    /// Imports an access key a second launch was given, as a file opened with the app: the key's server becomes the chosen
    /// one.
    /// </summary>
    /// <param name="accessKey">The access key.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// It may be called from any thread. Before the run has the app, a UI may keep the key for the run's start, as
    /// Avalonia's does, or drop it; the task may complete once the request is queued, and the token may not be read.
    /// Whether a key that cannot be read is shown or only logged is the UI's.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, for a UI whose task waits for the import.
    /// </exception>
    Task ImportAccessKey(string accessKey, CancellationToken cancellationToken);

    /// <summary>
    /// Connects as the UI's Connect button does, for a second launch that is a Quick Launch, the Store edition's own entry
    /// in Start, unless the app is connected or on its way.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// It may be called from any thread. Before the run has the app, a UI may keep the request for the run's start, as
    /// Avalonia's does, or drop it; the task may complete once the request is queued, and the token may not be read.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, for a UI whose task waits for the connect.
    /// </exception>
    Task QuickLaunch(CancellationToken cancellationToken);
}
