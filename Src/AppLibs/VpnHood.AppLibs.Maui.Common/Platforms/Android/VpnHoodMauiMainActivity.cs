using Android.OS;
using VpnHood.AppLibs.Droid.Common.Activities;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppLibs.Maui.Common;

public abstract class VpnHoodMauiMainActivity : MauiActivityEvent
{
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CreateMainActivityHandler();
        base.OnCreate(savedInstanceState);
    }
}