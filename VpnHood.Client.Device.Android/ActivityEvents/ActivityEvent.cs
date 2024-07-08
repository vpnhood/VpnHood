using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Runtime;
using Android.Views;

namespace VpnHood.Client.Device.Droid.ActivityEvents;

public class ActivityEvent : Activity, IActivityEvent
{
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CreateEvent?.Invoke(this, new CreateEventArgs
        {
            SavedInstanceState = savedInstanceState
        });
    }

    protected override void OnNewIntent(Intent? intent)
    {
        NewIntentEvent?.Invoke(this, new NewIntentEventArgs
        {
            Intent = intent
        });
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        RequestPermissionsResultEvent?.Invoke(this, new RequestPermissionsResultArgs
        {
            RequestCode = requestCode,
            Permissions = permissions,
            GrantResults = grantResults
        });

        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        var args = new KeyDownArgs
        {
            KeyCode = keyCode,
            KeyEvent = e
        };

        KeyDownEvent?.Invoke(this, args);
        return args.IsHandled ? args.IsHandled : base.OnKeyDown(keyCode, e);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        ActivityResultEvent?.Invoke(this, new ActivityResultEventArgs
        {
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
        base.OnDestroy();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ConfigurationChangedEvent?.Invoke(this, newConfig);
    }
}
