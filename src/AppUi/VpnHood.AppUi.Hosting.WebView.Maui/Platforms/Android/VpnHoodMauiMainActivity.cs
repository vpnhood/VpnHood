using Android.OS;
using VpnHood.AppLib.App.Android.Activities;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppUi.Hosting.WebView.Maui;

public abstract class VpnHoodMauiMainActivity : MauiActivityEvent
{
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CreateMainActivityHandler();
        base.OnCreate(savedInstanceState);
    }
}