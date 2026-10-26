using Avalonia;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The Avalonia UI as a desktop head names it (DesktopInitParams.Ui): TUi in AvaloniaDesktopHost's
// window with its tray - the window shown at once, or kept back until the tray or a second launch asks
// for it - against whatever app the head's host hands over: a service over loopback, or the app in the
// UI's own process; or, with no app to hand over, one message in its place. A cancelled run shuts the
// window's lifetime down.
public class AvaloniaDesktopUi<TUi> : IDesktopUi
    where TUi : Application, IAvaloniaUi, new()
{
    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        using var registration = cancellationToken.Register(AvaloniaDesktopHost.Shutdown);
        if (uiParams.AccessKey is { } accessKey)
            AvaloniaDesktopHost.ImportAccessKey(accessKey);
        AvaloniaDesktopHost.Run<TUi>([], uiParams.Api, uiParams.UiAssetProvider, useTray: true,
            startHidden: uiParams.StartHidden, cancellationToken);
    }

    public void RunMessage(DesktopUiMessageParams messageParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        using var registration = cancellationToken.Register(AvaloniaDesktopHost.Shutdown);
        AvaloniaDesktopHost.RunMessage<TUi>(messageParams);
    }

    public Task BringToFront(CancellationToken cancellationToken)
    {
        AvaloniaDesktopHost.ShowMainWindow();
        return Task.CompletedTask;
    }

    public Task ImportAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        AvaloniaDesktopHost.ImportAccessKey(accessKey);
        return Task.CompletedTask;
    }
}
