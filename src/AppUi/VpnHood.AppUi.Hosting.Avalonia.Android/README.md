# The Avalonia UI on Android

The activity that hosts `VpnHood.AppUi.Hosting.Avalonia` on Android with the app's activity-event
contract, so the app can ask for VPN permission and take access-key intents. A head's launcher
activity IS this one: it derives from `AndroidAvaloniaMainActivity<TUi>` and gets these pages.

The theme comes with this package (`Resources/values/themes.xml`, `Theme.VpnHood.Avalonia`) and a
head names it. Its parent is `Theme.AppCompat.NoActionBar` for one reason: Avalonia's own
`AvaloniaActivity` derives from `AppCompatActivity`, which refuses to start under a theme from any
other family. Nothing of AppCompat is asked for beyond that parent, and a head that wants a
cold-start background sets it there, since the launcher wears this theme until the first frame.

A head declares two things:

```csharp
// Avalonia 12 starts from the process's Application, so the head's is Avalonia's
[Application(...)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<VpnHoodAvaloniaApp>(javaReference, transfer)
{
    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateAppOptions);   // the app first
        AndroidAvaloniaUi.Init();                    // then the UI is given the app's API
        base.OnCreate();                            // then Avalonia
    }
}

// the launcher, and this UI's activity: one class, wearing this package's theme
[Activity(MainLauncher = true, Theme = "@style/Theme.VpnHood.Avalonia", ...)]
public class MainActivity : AndroidAvaloniaMainActivity<VpnHoodAvaloniaApp>
{
    // the head's access keys, when it takes any
    protected override AndroidMainActivityOptions CreateActivityOptions() => new() { ... };
}
```

The UI never touches the app itself: it reads and acts through the app's API (`AppData`, the
six controller interfaces of the web server), which in process is the controllers called
directly - no listener, no JSON - and in a paired browser is the same API over HTTP. That is
why `AndroidAvaloniaUi.Init` runs before Avalonia: the features it reads decide the theme, which
Avalonia applies as it initializes.

Avalonia initializes in every process of the app - the VPN service's and the quick tile's too,
since it starts from the Application - but a view is made only in the one with an activity:
`VpnHoodAvaloniaApp` hands the activity a view factory, and the activity names the bundle's assets
folder (the web server extracts the bundle) and registers the fonts (`AppData.Configure`) before
it asks for the view.
