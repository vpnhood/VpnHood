using System.Diagnostics.CodeAnalysis;
using Android.Content;
using Android.Content.PM;
using Android.Service.QuickSettings;
using Android.Views;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Common.Activities;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Client.App.Store;

namespace VpnHood.Client.App.Droid.Connect;

[Activity(Label = "@string/app_name",
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
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class MainActivity : AndroidAppWebViewMainActivity
{
    protected override bool ListenToAllIps => AssemblyInfo.ListenToAllIps;
    protected override int? DefaultSpaPort => AssemblyInfo.DefaultSpaPort;

    protected override IAppUpdaterService CreateAppUpdaterService()
    {
        return new GooglePlayAppUpdaterService(this);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        //Android.Gms.Ads.MobileAds.Initialize(this); // for ads

        var googlePlayAuthenticationService = new GooglePlayAuthenticationService(this, AssemblyInfo.FirebaseClientId);
        var authenticationService = new AppAuthenticationService(AssemblyInfo.StoreBaseUri, AssemblyInfo.StoreAppId, googlePlayAuthenticationService, AssemblyInfo.IsDebugMode);
        var googlePlayBillingService = GooglePlayBillingService.Create(this, authenticationService);
        VpnHoodApp.Instance.AccountService = new AppAccountService(authenticationService, googlePlayBillingService, AssemblyInfo.StoreAppId);
    }

    protected override void OnDestroy()
    {
        VpnHoodApp.Instance.AccountService = null;
        base.OnDestroy();
    }

}