using Android.Views;
using VpnHood.Core.Client.Device.Droid.Utils;

namespace VpnHood.AppLib.Droid.Common.Activities;

internal class WebViewWindowInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
    {
        // Set the background color
        var backgroundColor = VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor?.ToAndroidColor();
        if (backgroundColor != null)
            v.SetBackgroundColor(backgroundColor.Value);

        if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
            // Get the insets for the IME and system bars
            var mask = 0;
            if (insets.IsVisible(WindowInsets.Type.Ime()))
                mask |= WindowInsets.Type.Ime();

            // Adjust for system bars
            if (VpnHoodApp.Instance.Features.AdjustForSystemBars)
                mask |= WindowInsets.Type.SystemBars();

            // Apply padding to prevent layout overlap with system bars
            var rect = insets.GetInsets(mask);
            v.SetPadding(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return WindowInsets.Consumed; // Consume the insets

        }

        return insets; // Consume the insets
    }
}