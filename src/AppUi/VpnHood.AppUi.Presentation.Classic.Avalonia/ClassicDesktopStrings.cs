using System.ComponentModel;
using System.Globalization;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia;

// The words a desktop host shows around this UI, out of the UI's own. The event is the words' own, so a host that
// unsubscribes leaves nothing behind.
internal sealed class ClassicDesktopStrings : IDesktopStrings
{
    public event PropertyChangedEventHandler? PropertyChanged {
        add => Strings.Current.PropertyChanged += value;
        remove => Strings.Current.PropertyChanged -= value;
    }

    public string Open => Strings.Current.Open;
    public string Connect => Strings.Current.Connect;
    public string Disconnect => Strings.Current.Disconnect;
    public string ExitAndDisconnect => Strings.Current.ExitAndDisconnect;
    public string KillSwitchBlockingMessage => Strings.Current.KillSwitchBlockingMsg;
    public string AdministratorsOnly => Strings.Current.AdministratorsOnly;
    public string Close => Strings.Current.Close;
    public bool IsRightToLeft => Strings.Current.IsRightToLeft;

    public string ConnectionStateText(AppConnectionState connectionState)
    {
        return Strings.Current.ConnectionStateText(connectionState);
    }

    public Task SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken)
    {
        return Strings.Current.SetCultureAsync(culture, cancellationToken);
    }
}
