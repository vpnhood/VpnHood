using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.Net;
using VpnHood.Client.Device.Android;
using Android.OS;
using Android.Runtime;

namespace VpnHood.Client.App.Maui;

[Activity(
    Theme = "@style/Maui.SplashTheme", 
    MainLauncher = true,
    Exported = true,
    AlwaysRetainTaskState = true, 
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges =
        ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density |
        ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
        ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher })]
public class MainActivity : MauiAppCompatActivity
{
    private const int RequestVpnPermission = 10;
    private AndroidDevice Device => AndroidDevice.Current ?? throw new InvalidOperationException("AndroidDevice has not been initialized.");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // manage VpnPermission
        Device.OnRequestVpnPermission += Device_OnRequestVpnPermission;

    }

    private void Device_OnRequestVpnPermission(object? sender, EventArgs e)
    {
        var intent = VpnService.Prepare(this);
        if (intent == null)
            Device.VpnPermissionGranted();
        else
            StartActivityForResult(intent, RequestVpnPermission);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        if (requestCode == RequestVpnPermission && resultCode == Result.Ok)
            Device.VpnPermissionGranted();
        else
            Device.VpnPermissionRejected();
    }

    protected override void OnDestroy()
    {
        Device.OnRequestVpnPermission -= Device_OnRequestVpnPermission;
        base.OnDestroy();
    }
}