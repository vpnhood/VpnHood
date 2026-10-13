using Android.Window;

namespace VpnHood.AppLib.App.Android.Utils;

public sealed class AndroidBackInvokedCallback(Action onBackInvoked)
    : Java.Lang.Object, IOnBackInvokedCallback
{
    public void OnBackInvoked() => onBackInvoked();
}