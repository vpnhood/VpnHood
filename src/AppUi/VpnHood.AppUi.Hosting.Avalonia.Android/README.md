# The Avalonia UI on Android

The Application and the activity that host `VpnHood.AppUi.Hosting.Avalonia` on Android. The
Application starts the app and hands the UI its API; the activity speaks the app's activity-event
contract, so the app can ask for VPN permission and take access-key intents. A head's Application
and launcher activity ARE these two: they derive from `AndroidAvaloniaApplication<TUi>` and
`AndroidAvaloniaMainActivity<TUi>` and get these pages.

The theme comes with this package (`Resources/values/themes.xml`, `Theme.VpnHood.Avalonia`) and a
head names it. Its parent is `Theme.AppCompat.NoActionBar` for one reason: Avalonia's own
`AvaloniaActivity` derives from `AppCompatActivity`, which refuses to start under a theme from any
other family. Nothing of AppCompat is asked for beyond that parent, and a head that wants a
cold-start background sets it there, since the launcher wears this theme until the first frame.

A head declares two things:

```csharp
// Avalonia 12 starts from the process's Application: the app first, then its API to the UI,
// then Avalonia - all in the base class
[Application(...)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AndroidAvaloniaApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    // asked for only in the app's own process, never in the VPN service's or the tile's
    protected override AppInitParams CreateInitParams() => new() { ... };
}

// the launcher, and this UI's activity: one class, wearing this package's theme
[Activity(MainLauncher = true, Theme = "@style/Theme.VpnHood.Avalonia", ...)]
public class MainActivity : AndroidAvaloniaMainActivity<ClassicAvaloniaApp>
{
    // the head's access keys, when it takes any
    protected override AndroidMainActivityOptions CreateActivityOptions() => new() { ... };
}
```

The UI never touches the app itself: it reads and acts through the app's API (`VhApp`, the six
controller interfaces of the web server), which in process is the controllers called directly -
no listener, no JSON - and in a paired browser is the same API over HTTP. That is why the
Application hands the UI the API before Avalonia initializes: the features it reads decide the
theme, which Avalonia applies as it initializes.

Avalonia initializes in every process of the app - the VPN service's and the quick tile's too,
since it starts from the Application - but a view is made only in the one with an activity:
`VpnHoodAvaloniaAppBase` hands the activity a view factory, and the activity prepares the UI's
content - the words, and the fonts it registers - and tells the app the UI's languages before it
asks for the view. Both halves are the one start every Avalonia host runs
(`AvaloniaUiHosting`), split at the two moments Android's lifecycle gives.
