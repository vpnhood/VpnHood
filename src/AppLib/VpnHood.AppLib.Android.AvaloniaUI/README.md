# The Avalonia UI on Android

For an Android whose web view cannot run the web UI: a TV whose web view is too old, a phone whose
web view is broken. The UI itself is `VpnHood.AppLib.AvaloniaUI`; this project is the activity
that hosts it with the app's activity-event contract, so the app can ask for VPN permission and
take access-key intents exactly as it does behind the web UI.

Avalonia 12 needs the process's `Application` to be its own, so a head that opts in declares:

```csharp
[Application(...)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<VpnHoodAvaloniaApp>(javaReference, transfer)
{
    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateAppOptions);   // as today
        base.OnCreate();                            // then Avalonia
    }
}

[Activity(MainLauncher = true, Theme = "@style/Theme.AppCompat.NoActionBar", ...)]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
public class MainActivity : AndroidAppAvaloniaMainActivity
{
    protected override AndroidMainActivityOptions CreateActivityOptions() => new() { ... };
}
```

Which head, and whether the web UI's activity and this one ship in one bundle with a runtime
choice (the web view's version, `AndroidSpaWebView.GetWebViewVersion`) or as two heads, is the
packaging decision the TV plan leaves open (§6 of the plan's "one TV UI or two"). Nothing here
decides it.
