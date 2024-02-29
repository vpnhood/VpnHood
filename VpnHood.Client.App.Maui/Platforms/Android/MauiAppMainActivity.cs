using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using VpnHood.Client.App.Droid.Common.Activities;

namespace VpnHood.Client.Samples.MauiAppSpaSample;

public abstract class MauiAppMainActivity : MauiAppCompatActivity 
{
    private AndroidAppMainActivityHandler _mainActivityHandler = default!;
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _mainActivityHandler = CreateMainActivityHandler();
        _mainActivityHandler.OnCreate(savedInstanceState);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        if (!_mainActivityHandler.OnNewIntent(intent))
            base.OnNewIntent(intent);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        _mainActivityHandler.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        _mainActivityHandler.OnActivityResult(requestCode, resultCode, data);
        base.OnActivityResult(requestCode, resultCode, data);
    }

    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        return _mainActivityHandler.OnKeyDown(keyCode, e) || base.OnKeyDown(keyCode, e);
    }

    protected override void OnDestroy()
    {
        _mainActivityHandler.OnDestroy();
        base.OnDestroy();
    }
}