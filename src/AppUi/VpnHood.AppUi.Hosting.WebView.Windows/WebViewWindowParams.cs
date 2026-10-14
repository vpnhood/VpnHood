using VpnHood.AppLib.Api;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.WebHosting;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.AppUi.Hosting.WebView.Branding;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// What the window is drawn with, read from the app before it is made (WindowsWebViewUi).
internal class WebViewWindowParams
{
    public required VpnHoodApi Api { get; init; }
    public required IAppWebHost WebHost { get; init; }
    public required string AppName { get; init; }
    public required bool IsTv { get; init; }

    // the web UI's branding, for the window's ground and its title bar
    public required AppBrandingColors Colors { get; init; }

    // the shape the window takes on its screen (DesktopWindowFit)
    public required DesktopWindowFitOptions WindowFit { get; init; }

    // the library's own badge icons
    public required AppResources Resources { get; init; }

    // the web view's profile, in the person's own folder
    public required string WebViewDataPath { get; init; }
}
