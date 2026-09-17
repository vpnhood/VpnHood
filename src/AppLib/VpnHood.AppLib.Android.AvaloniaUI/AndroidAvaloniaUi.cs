using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.Api.WebHost;

namespace VpnHood.AppLib.Droid.AvaloniaUI;

// The UI's step in a head's Application.OnCreate, between the app and Avalonia: the app's API,
// handed to the UI in the process that has an app - the VPN service's and the quick tile's have
// none and get no view - before Avalonia initializes and reads the features for its theme. In
// process the API is the app's own controllers, and the read completes at once.
public static class AndroidAvaloniaUi
{
    public static void Init()
    {
        if (!VpnHoodApp.IsInit)
            return;

        AppModel.Init(VpnHoodApp.Instance.Api, CancellationToken.None).GetAwaiter().GetResult();
    }
}
