using Android.Window;

namespace VpnHood.AppLib.App.Android.Utils;

// A class of its own, made only on Android 13+: an older Android cannot even load a class that
// implements this interface, so an activity never implements it itself.
public sealed class AndroidBackInvokedCallback(Action onBackInvoked)
    : Java.Lang.Object, IOnBackInvokedCallback
{
    public void OnBackInvoked() => onBackInvoked();
}