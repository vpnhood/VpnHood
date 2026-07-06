using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Ios.Common;

// iOS implementation of the device UI provider. Most platform-integration points (quick launch,
// notifications, private DNS, kill switch) are not applicable to a regular iOS VPN app, so they
// fall back to the Null implementation. We only wire up opening the iOS Settings app.
public class IosDeviceUiProvider : NullDeviceUiProvider
{
    // The iOS WebView renders edge-to-edge (see IosSpaWebViewController), so the SPA needs the
    // status-bar / home-indicator inset sizes to pad itself. Mirrors AndroidDeviceUiProvider.GetBarsInfo.
    // Heights are reported in PHYSICAL PIXELS (points * screen scale) because the SPA divides by
    // window.devicePixelRatio (== UIScreen.Scale) to convert back to CSS points.
    public override SystemBarsInfo GetBarsInfo(IUiContext uiContext)
    {
        // BuildAppState may call this off the main thread; SafeAreaInsets must be read on the UI thread.
        return IosUtils.RunOnUiThread(() => {
            var window = GetKeyWindow();
            if (window == null)
                return SystemBarsInfo.Default;

            var insets = window.SafeAreaInsets;
            var scale = window.Screen.Scale;
            return new SystemBarsInfo {
                TopHeight = (int)Math.Ceiling(insets.Top * scale),
                BottomHeight = (int)Math.Ceiling(insets.Bottom * scale)
            };
        }).GetAwaiter().GetResult();
    }

    public override bool IsAppSettingsSupported => true;

    public override void OpenAppSettings(IUiContext context)
    {
        OpenUrl(UIApplication.OpenSettingsUrlString);
    }

    public override bool IsSettingsSupported => true;

    public override void OpenSettings(IUiContext uiContext)
    {
        OpenUrl(UIApplication.OpenSettingsUrlString);
    }

    private static void OpenUrl(string urlString)
    {
        var url = new NSUrl(urlString);
        UIApplication.SharedApplication.OpenUrl(url, new NSDictionary(), null);
    }

    // The active foreground key window across the connected scenes (the SPA's window).
    private static UIWindow? GetKeyWindow()
    {
        return UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .Where(scene => scene.ActivationState == UISceneActivationState.ForegroundActive)
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)
            ?? UIApplication.SharedApplication.Windows.FirstOrDefault(w => w.IsKeyWindow);
    }
}
