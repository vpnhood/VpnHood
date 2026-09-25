using Avalonia;
using VpnHood.AppUi.Hosting.Abstractions;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The Avalonia UI as a desktop head names it (CliHeadParams.Ui): TUi in AvaloniaDesktopHost's
// window - shown at once, or kept back until the tray asks for it - against whatever app the head's
// host hands over: a service over loopback, or the app in the UI's own process. A cancelled run
// shuts the window's lifetime down.
public class AvaloniaDesktopHost<TUi> : IDesktopUi
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
}
