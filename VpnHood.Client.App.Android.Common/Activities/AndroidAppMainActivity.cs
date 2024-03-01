using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.Common.Activities;

public abstract class AndroidAppMainActivity : Activity
{
    protected IActivityEvent ActivityEvent => MainActivityHandler;
    protected AndroidAppMainActivityHandler MainActivityHandler = default!;
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MainActivityHandler = CreateMainActivityHandler();
        MainActivityHandler.OnCreate(savedInstanceState);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        if (!MainActivityHandler.OnNewIntent(intent))
            base.OnNewIntent(intent);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        MainActivityHandler.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        MainActivityHandler.OnActivityResult(requestCode, resultCode, data);
        base.OnActivityResult(requestCode, resultCode, data);
    }

    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        return MainActivityHandler.OnKeyDown(keyCode, e) || base.OnKeyDown(keyCode, e);
    }

    protected override void OnDestroy()
    {
        MainActivityHandler.OnDestroy();
        base.OnDestroy();
    }
}
