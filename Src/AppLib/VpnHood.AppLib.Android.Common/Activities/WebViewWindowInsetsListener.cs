using Android.Views;
using VpnHood.Core.Client.Device.Droid.Utils;

namespace VpnHood.AppLib.Droid.Common.Activities;

internal class WebViewWindowInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
    {
        // Get the insets for the IME and system bars
        var mask = WindowInsets.Type.Ime();
        if (VpnHoodApp.Instance.Features.AdjustForSystemBars)
            mask |= WindowInsets.Type.SystemBars();

        // Apply padding to prevent layout overlap with system bars
        var rect = insets.GetInsets(mask);
        v.SetPadding(rect.Left, rect.Top, rect.Right, rect.Bottom);

        // Set the background color
        var backgroundColor = VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor?.ToAndroidColor();
        if (backgroundColor != null)
            v.SetBackgroundColor(backgroundColor.Value);

        return WindowInsets.Consumed; // Consume the insets
    }
}