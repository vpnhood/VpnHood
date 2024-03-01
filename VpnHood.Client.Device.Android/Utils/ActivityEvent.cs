using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;

namespace VpnHood.Client.Device.Droid.Utils;

public class ActivityEvent : Activity, IActivityEvent
{
    public event EventHandler<ActivityResultEventArgs>? OnActivityResultEvent;
    public event EventHandler<CreateEventArgs>? OnCreateEvent;
    public event EventHandler<NewIntentEventArgs>? OnNewIntentEvent;
    public event EventHandler<RequestPermissionsResultArgs>? OnRequestPermissionsResultEvent;
    public event EventHandler<KeyDownArgs>? OnKeyDownEvent;
    public event EventHandler? OnDestroyEvent;
    public Activity Activity => this;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnCreateEvent?.Invoke(this, new CreateEventArgs
        {
            SavedInstanceState = savedInstanceState
        });
    }

    protected override void OnNewIntent(Intent? intent)
    {
        OnNewIntentEvent?.Invoke(this, new NewIntentEventArgs
        {
            Intent = intent
        });
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        OnRequestPermissionsResultEvent?.Invoke(this, new RequestPermissionsResultArgs
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

        OnKeyDownEvent?.Invoke(this, args);
        return args.IsHandled ? args.IsHandled : base.OnKeyDown(keyCode, e);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        OnActivityResultEvent?.Invoke(this, new ActivityResultEventArgs
        {
            RequestCode = requestCode,
            ResultCode = resultCode,
            Data = data
        });

        base.OnActivityResult(requestCode, resultCode, data);
    }

    protected override void OnDestroy()
    {
        OnDestroyEvent?.Invoke(this, EventArgs.Empty);
        base.OnDestroy();
    }
}
