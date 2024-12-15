using Android.OS;
using VpnHood.AppLib.Droid.Common.Activities;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppLib.Maui.Common;

public abstract class VpnHoodMauiMainActivity : MauiActivityEvent
{
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CreateMainActivityHandler();
        base.OnCreate(savedInstanceState);
    }
}