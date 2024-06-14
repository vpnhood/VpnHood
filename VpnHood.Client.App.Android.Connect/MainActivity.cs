using Android.Content;
using Android.Content.PM;
using Android.Service.QuickSettings;
using Android.Views;
using Firebase;
using Firebase.Crashlytics;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Droid.Common.Activities;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App.Droid.Connect;

[Activity(
    Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize, // resize app when keyboard is shown
    LaunchMode = LaunchMode.SingleInstance, 
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([TileService.ActionQsTilePreferences])]
public class MainActivity : AndroidAppMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        // Initialize Firebase
        InitFirebaseCrashlytics();
    }

    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidAppWebViewMainActivityHandler(this, new AndroidMainActivityWebViewOptions
        {
            DefaultSpaPort = AppSettings.Instance.DefaultSpaPort,
            ListenToAllIps = AppSettings.Instance.ListenToAllIps
        });
    }

    private void InitFirebaseCrashlytics()
    {
        var firebaseOptions = new FirebaseOptions.Builder()
            .SetProjectId(AppSettings.Instance.FirebaseProjectId)
            .SetApplicationId(AppSettings.Instance.FirebaseApplicationId)
            .SetApiKey(AppSettings.Instance.FirebaseApiKey)
            .Build();

        var firebaseApp = FirebaseApp.InitializeApp(Application.Context, firebaseOptions);
        if (firebaseApp == null)
            VhLogger.Instance.LogError("The FirebaseApp is not initialized.");

        // Initialize Crashlytics
        FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(true);   
    }
}