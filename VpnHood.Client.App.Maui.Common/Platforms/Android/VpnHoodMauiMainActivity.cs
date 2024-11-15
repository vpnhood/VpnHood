using Android.OS;
using VpnHood.Client.App.Droid.Common.Activities;

// ReSharper disable once CheckNamespace
namespace VpnHood.Client.App.Maui.Common;

public abstract class VpnHoodMauiMainActivity : MauiActivityEvent
{
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CreateMainActivityHandler();
        base.OnCreate(savedInstanceState);
    }
}