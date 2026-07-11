using Android.Window;

namespace VpnHood.AppLib.Droid.Common.Utils;

internal sealed class AndroidBackInvokedCallback(Action onBackInvoked)
    : Java.Lang.Object, IOnBackInvokedCallback
{
    public void OnBackInvoked() => onBackInvoked();
}