using Android.Content;
using Android.Content.PM;
using Android.Service.QuickSettings;
using Android.Views;
using VpnHood.Client.App.Droid.Common.Activities;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Client.App.Store;

namespace VpnHood.Client.App.Droid.Connect;

[Activity(
    Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize, // resize app when keyboard is shown
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance, 
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([TileService.ActionQsTilePreferences])]
public class MainActivity : AndroidAppMainActivity
{
    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidAppWebViewMainActivityHandler(this, new AndroidMainActivityWebViewOptions
        {
            DefaultSpaPort = AssemblyInfo.DefaultSpaPort,
            ListenToAllIps = AssemblyInfo.ListenToAllIps,
            AppUpdaterService = new GooglePlayAppUpdaterService(this)
        });
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        var googlePlayAuthenticationService = new GooglePlayAuthenticationService(this, AssemblyInfo.FirebaseClientId);
        var authenticationService = new AppAuthenticationService(AssemblyInfo.StoreBaseUri, AssemblyInfo.StoreAppId, googlePlayAuthenticationService, AssemblyInfo.IsDebugMode);
        var googlePlayBillingService = GooglePlayBillingService.Create(this, authenticationService);

        // TODO enable for production with ad
        /*var googlePlayAdService = GooglePlayAdService.Create(this, AssemblyInfo.RewardedAdUnitId);

        if (VpnHoodApp.Instance.IsIdle && VpnHoodApp.Instance.ActiveClientProfile?.Token.IsAdRequired == true || true)
            _ = googlePlayAdService.LoadRewardedAd(CancellationToken.None);

        VpnHoodApp.Instance.AppAdService = googlePlayAdService;*/
        VpnHoodApp.Instance.AppUpdaterService = new GooglePlayAppUpdaterService(this);
        VpnHoodApp.Instance.AppAccountService = new AppAccountService(authenticationService, googlePlayBillingService, AssemblyInfo.StoreAppId);
    }

    protected override void OnDestroy()
    {
        VpnHoodApp.Instance.AppUpdaterService = null;
        VpnHoodApp.Instance.AppAccountService = null;
        VpnHoodApp.Instance.AppAdService = null;
        base.OnDestroy();
    }
}