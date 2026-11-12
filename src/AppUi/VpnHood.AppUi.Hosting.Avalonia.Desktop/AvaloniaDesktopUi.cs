using Avalonia;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

/// <summary>
/// Provides the Avalonia UI as a desktop head names it: the UI in <see cref="AvaloniaDesktopHost"/>'s window, against
/// whatever app the head's host hands over, or one message in its place where there is none.
/// </summary>
/// <typeparam name="TUi">The UI's application.</typeparam>
public class AvaloniaDesktopUi<TUi> : IDesktopUi
    where TUi : Application, IAvaloniaUi, new()
{
    /// <summary>Runs the UI's window until its run ends.</summary>
    /// <param name="uiParams">What the window runs against, and how.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>
    /// A token canceled before the call returns at once, and one canceled later ends the run. An access key or a Quick
    /// Launch the parameters carry goes to the UI as the run starts.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The parameters name no UI content store, or the UI made no main window or is not a
    /// <see cref="VpnHoodAvaloniaAppBase"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled while the run started.
    /// </exception>
    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        using var registration = cancellationToken.Register(AvaloniaDesktopHost.Shutdown);
        if (uiParams.AccessKey is { } accessKey)
            AvaloniaDesktopHost.ImportAccessKey(accessKey);
        if (uiParams.IsQuickLaunch)
            AvaloniaDesktopHost.QuickLaunch();
        AvaloniaDesktopHost.Run<TUi>([], uiParams.Api, uiParams.UiAssetProvider, useTray: uiParams.UseTray,
            keepRunningWhileConnected: uiParams.KeepRunningWhileConnected, shell: uiParams.Shell,
            startHidden: uiParams.StartHidden, cancellationToken);
    }

    /// <summary>Runs a window with one message in the UI's place, until it is closed.</summary>
    /// <param name="messageParams">The message.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>
    /// A token canceled before the call returns at once, and one canceled once the window runs ends it. Where the UI's
    /// content loads, a run only administrators may use gets the UI's own words, and the Close button the UI's word, in the
    /// device's language where the UI has it and else in English; otherwise the message is the host's text, with an
    /// English Close.
    /// </remarks>
    public void RunMessage(DesktopUiMessageParams messageParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        using var registration = cancellationToken.Register(AvaloniaDesktopHost.Shutdown);
        AvaloniaDesktopHost.RunMessage<TUi>(messageParams);
    }

    /// <summary>Asks for the window to show, as the tray's Open does.</summary>
    /// <param name="cancellationToken">Unused: the call only queues the request.</param>
    /// <returns>A task that is already complete.</returns>
    /// <remarks>
    /// The request goes to the UI thread, or waits for the run's start; the task does not wait for it, and the token is not
    /// read.
    /// </remarks>
    public Task BringToFront(CancellationToken cancellationToken)
    {
        AvaloniaDesktopHost.ShowMainWindow();
        return Task.CompletedTask;
    }

    /// <summary>Has the UI import an access key.</summary>
    /// <param name="accessKey">The access key.</param>
    /// <param name="cancellationToken">Unused: the call only queues the request.</param>
    /// <returns>A task that is already complete.</returns>
    /// <remarks>
    /// The request goes to the UI thread, or waits for the run's start; the task does not wait for it, and the token is not
    /// read.
    /// </remarks>
    public Task ImportAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        AvaloniaDesktopHost.ImportAccessKey(accessKey);
        return Task.CompletedTask;
    }

    /// <summary>Has the UI connect, as its Connect button does.</summary>
    /// <param name="cancellationToken">Unused: the call only queues the request.</param>
    /// <returns>A task that is already complete.</returns>
    /// <remarks>
    /// The request goes to the UI thread, or waits for the run's start; the task does not wait for it, and the token is not
    /// read.
    /// </remarks>
    public Task QuickLaunch(CancellationToken cancellationToken)
    {
        AvaloniaDesktopHost.QuickLaunch();
        return Task.CompletedTask;
    }
}
