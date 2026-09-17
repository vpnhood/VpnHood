using Android.Window;

namespace VpnHood.AppUi.Hosting.WebView.Droid;

internal sealed class AndroidBackInvokedCallback(Action onBackInvoked)
    : Java.Lang.Object, IOnBackInvokedCallback
{
    public void OnBackInvoked() => onBackInvoked();
}