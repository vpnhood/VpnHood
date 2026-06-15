using Foundation;
using UIKit;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Ios.Common;

// iOS implementation of the device UI provider. Most platform-integration points (quick launch,
// notifications, private DNS, kill switch) are not applicable to a regular iOS VPN app, so they
// fall back to the Null implementation. We only wire up opening the iOS Settings app.
public class IosDeviceUiProvider : NullDeviceUiProvider
{
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
}
