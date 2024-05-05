using System.Drawing;
using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Client.App.Droid.GooglePlay.Ads;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Store;

namespace VpnHood.Client.App.Droid.Connect;

[Application(
    Label = "@string/app_name",
    Icon = "@mipmap/appicon",
    Banner = "@mipmap/banner", // for TV
    NetworkSecurityConfig = "@xml/network_security_config",  // required for localhost
    SupportsRtl = true, AllowBackup = true)]

[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = "ca-app-pub-8662231806304184~1740102860")]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions()
    {
        var googlePlayAuthenticationService = new GooglePlayAuthenticationService(AssemblyInfo.FirebaseClientId);
        var authenticationService = new AppAuthenticationService(AssemblyInfo.StoreBaseUri, AssemblyInfo.StoreAppId, googlePlayAuthenticationService, AssemblyInfo.IsDebugMode);
        var googlePlayBillingService = new GooglePlayBillingService(authenticationService);

        var resources = DefaultAppResource.Resource;
        resources.Colors.NavigationBarColor = Color.FromArgb(100, 32, 25, 81);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(100, 32, 25, 81);

        return new AppOptions
        {
            AccessKeys = [AssemblyInfo.GlobalServersAccessKey],
            Resource = DefaultAppResource.Resource,
            UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdaterService = new GooglePlayAppUpdaterService(),
            CultureService = AndroidAppAppCultureService.CreateIfSupported(),
            AccountService = new AppAccountService(authenticationService, googlePlayBillingService, AssemblyInfo.StoreAppId),
            AdService = GooglePlayAdService.Create(AssemblyInfo.RewardedAdUnitId),
            UiService = new AndroidAppUiService()
        };
    }
}

