// this file sits in a namespace called Avalonia, so the framework type needs an explicit name
using AvaloniaApplication = Avalonia.Application;
using VpnHood.AppLib.App.Android.Activities;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Avalonia.Android;

// The access key a link or a file opens the activity with goes to the UI, which imports it as on iOS and the
// desktop: it waits while the consent page holds the app, and says it in the UI's snackbar, not a toast.
internal class AndroidAvaloniaMainActivityHandler(
    IActivityEvent activityEvent,
    AndroidMainActivityOptions options,
    Task<VhColor?> chromeColor)
    : AndroidAppMainActivityHandler(activityEvent, options, chromeColor)
{
    protected override void ImportAccessKey(string accessKey)
    {
        var ui = AvaloniaApplication.Current as VpnHoodAvaloniaAppBase ??
                 throw new InvalidOperationException("The Avalonia UI is not the app's.");
        _ = ui.ImportAccessKey(accessKey);
    }
}
