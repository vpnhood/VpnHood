using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Runtime;
using Android.Views;
using Avalonia.Android;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Client.Devices.Droid.ActivityEvents;

namespace VpnHood.AppLib.Droid.AvaloniaUI;

// The Avalonia UI's activity. Two base classes cannot be had at once, so this is Avalonia's main
// activity speaking the app's activity-event contract itself - what ActivityEvent does for the web
// UI's activity - and AndroidAppMainActivityHandler does the rest by composition: the UI context
// the app needs to ask for VPN permission, the access-key intents, the activity results. The Back
// key, a remote's or a phone's, reaches Avalonia through AvaloniaActivity.OnBackPressed, which
// MainView answers.
public class AndroidAppAvaloniaMainActivity<TUi> : AvaloniaMainActivity, IActivityEvent
    where TUi : Avalonia.Application, IAvaloniaUi
{
    protected AndroidAppMainActivityHandler? MainActivityHandler { get; private set; }

    public event EventHandler<ActivityResultEventArgs>? ActivityResultEvent;
    public event EventHandler<CreateEventArgs>? CreateEvent;
    public event EventHandler<NewIntentEventArgs>? NewIntentEvent;
    public event EventHandler<RequestPermissionsResultArgs>? RequestPermissionsResultEvent;
    public event EventHandler<KeyDownArgs>? KeyDownEvent;
    public event EventHandler<Configuration>? ConfigurationChangedEvent;
    public event EventHandler? PauseEvent;
    public event EventHandler? ResumeEvent;
    public event EventHandler? DestroyEvent;
    public Activity Activity => this;

    // The access-key schemes and mimes a head takes; none by default.
    protected virtual AndroidMainActivityOptions CreateActivityOptions()
    {
        return new AndroidMainActivityOptions();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // before base.OnCreate, so the handler is subscribed when the create event fires
        MainActivityHandler = new AndroidAppMainActivityHandler(this, CreateActivityOptions());

        // Also before base.OnCreate, which makes the view (the UI hands this activity its
        // factory): a UI whose pictures are files reads them from a folder, which on Android is a
        // copy out of the package (AndroidAppContent), made on the first run of a version - on
        // this thread, deliberately, as every frame after this line depends on it. PrepareContent
        // also registers the UI's fonts, Avalonia having started with the process's Application,
        // before the folder could be read. Then the app is told which languages the UI has; in
        // process that completes at once. The web server is what a phone pairs with, so it comes
        // up here rather than on the pairing screen.
        if (!VpnHoodAppWebServer.IsInit)
            VpnHoodAppWebServer.Init(VpnHoodApp.Instance);
        TUi.PrepareContent();
        AppModel.Configure(TUi.AvailableCultures, CancellationToken.None).GetAwaiter().GetResult();

        base.OnCreate(savedInstanceState);
        CreateEvent?.Invoke(this, new CreateEventArgs { SavedInstanceState = savedInstanceState });
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        NewIntentEvent?.Invoke(this, new NewIntentEventArgs { Intent = intent });
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        RequestPermissionsResultEvent?.Invoke(this, new RequestPermissionsResultArgs {
            RequestCode = requestCode,
            Permissions = permissions,
            GrantResults = grantResults
        });
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        var args = new KeyDownArgs { KeyCode = keyCode, KeyEvent = e };
        KeyDownEvent?.Invoke(this, args);
        return args.IsHandled || base.OnKeyDown(keyCode, e);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        ActivityResultEvent?.Invoke(this, new ActivityResultEventArgs {
            RequestCode = requestCode,
            ResultCode = resultCode,
            Data = data
        });
        base.OnActivityResult(requestCode, resultCode, data);
    }

    protected override void OnResume()
    {
        base.OnResume();
        ResumeEvent?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPause()
    {
        PauseEvent?.Invoke(this, EventArgs.Empty);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        DestroyEvent?.Invoke(this, EventArgs.Empty);
        MainActivityHandler = null;
        base.OnDestroy();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ConfigurationChangedEvent?.Invoke(this, newConfig);
    }
}
