using Avalonia;
using VpnHood.AppUi.Hosting.Abstractions;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The Avalonia UI as a desktop head names it (DesktopInitParams.Ui): TUi in AvaloniaDesktopHost's
// window - shown at once, or kept back until the tray asks for it - against whatever app the head's
// host hands over: a service over loopback, or the app in the UI's own process; or, with no app to
// hand over, one message in its place. A cancelled run shuts the window's lifetime down.
public class AvaloniaDesktopUi<TUi> : IDesktopUi
    where TUi : Application, IAvaloniaUi, new()
{
    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        using var registration = cancellationToken.Register(AvaloniaDesktopHost.Shutdown);
        AvaloniaDesktopHost.Run<TUi>([], showWindow: !uiParams.StartHidden, uiParams.Api, uiParams.UiAssetProvider,
            uiParams.ExitOnClose);
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
}
