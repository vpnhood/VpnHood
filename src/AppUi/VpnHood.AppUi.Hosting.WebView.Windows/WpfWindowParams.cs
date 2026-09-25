using VpnHood.AppLib.Api;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.WebHosting;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// What the window is drawn with, read from the app before it is made (WpfWebViewUi).
internal class WpfWindowParams
{
    public required VpnHoodApi Api { get; init; }
    public required IAppWebHost WebHost { get; init; }
    public required string AppName { get; init; }
    public required bool IsTv { get; init; }

    // the look out of the UI's store, and the library's own badge icons
    public required AppResources Resources { get; init; }

    // the web view's profile, in the person's own folder
    public required string WebViewDataPath { get; init; }

    public required bool ExitOnClose { get; init; }
    public required bool StartHidden { get; init; }
}
