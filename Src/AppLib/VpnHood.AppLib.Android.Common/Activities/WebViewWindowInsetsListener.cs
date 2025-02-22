using Android.Views;
using VpnHood.Core.Client.Device.Droid.Utils;

namespace VpnHood.AppLib.Droid.Common.Activities;

internal class WebViewWindowInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
    {
        var statusBarHeight = insets.GetInsets(WindowInsets.Type.StatusBars()).Top;
        var navBarHeight = insets.GetInsets(WindowInsets.Type.NavigationBars()).Bottom;

        // Apply padding to prevent layout overlap with system bars
        if (VpnHoodApp.Instance.Features.AdjustForSystemBars)
            v.SetPadding(0, statusBarHeight, 0, navBarHeight);

        var backgroundColor = VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor?.ToAndroidColor();
        if (backgroundColor != null)
            v.SetBackgroundColor(backgroundColor.Value);

        return WindowInsets.Consumed; // Consume the insets
    }
}