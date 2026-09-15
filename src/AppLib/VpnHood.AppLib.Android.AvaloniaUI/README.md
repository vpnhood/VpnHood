# The Avalonia UI on Android

The activity that hosts `VpnHood.AppLib.AvaloniaUI` on Android with the app's activity-event
contract, so the app can ask for VPN permission and take access-key intents exactly as it does
behind the web UI. Every head ships it beside the web view, and the app chooses between the two
at launch - today only by the debug command `/avalonia-ui` (`DebugCommands.AvaloniaUi`), which
is also where a device's web-view version would be judged too old for the web UI, when that
check is written.

A head declares three things:

```csharp
// Avalonia 12 starts from the process's Application, so the head's is Avalonia's
[Application(...)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<VpnHoodAvaloniaApp>(javaReference, transfer)
{
    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateAppOptions);   // the app first
        base.OnCreate();                            // then Avalonia
    }
}

// the Avalonia UI's activity: no launcher entry, not exported, AppCompat's theme
[Activity(Theme = "@style/Theme.AppCompat.NoActionBar", Exported = false, ...)]
public class AvaloniaActivity : AndroidAppAvaloniaMainActivity
{
    // the head's access keys, when it takes any
    protected override AndroidMainActivityOptions CreateActivityOptions() => new() { ... };
}

// the web view's launcher activity hands the launch over when the app asks
public class MainActivity : AndroidAppMainActivity
{
    protected override Type? RedirectActivityType =>
        VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi) ? typeof(AvaloniaActivity) : null;
    ...
}
```

Avalonia initializes in every process of the app - the VPN service's and the quick tile's too,
since it starts from the Application - but a view is made only in the one with an activity:
`VpnHoodAvaloniaApp` hands the activity a view factory, and the activity names the bundle's assets
folder (the web server extracts the bundle) and registers the fonts before it asks for the view.
