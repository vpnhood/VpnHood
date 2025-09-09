using Android.Graphics;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.Common.Utils;

public static class WindowInsetUtils
{
    public static void Configure(Window? window, 
        bool lightStatusBars, bool lightNavBars, bool navigationBarContrastEnforced)
    {
        if (window == null) {
            VhLogger.Instance.LogWarning("Activity window is null, can not initialize the window.");
            return;
        }

        // set bar icon appearance
        SetBarIconAppearance(window, lightStatusBars: lightStatusBars, lightNavBars: lightNavBars);

        // keep your blue pure
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            window.NavigationBarContrastEnforced = navigationBarContrastEnforced;

        // set window insets listener for API 35
        if (OperatingSystem.IsAndroidVersionAtLeast(35)) {
            var contentRoot = window.DecorView;
            contentRoot.SetOnApplyWindowInsetsListener(new WindowInsetsListener());
            contentRoot.RequestApplyInsets();
        }
        // set window insets listener for API 30
        else if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
            // Android 11–14: valid here (deprecated only on 35+)
            window.SetDecorFitsSystemWindows(false); 
            window.SetStatusBarColor(Color.Transparent); 
            window.SetNavigationBarColor(Color.Transparent); 

            var contentRoot = window.DecorView;
            contentRoot.SetOnApplyWindowInsetsListener(new WindowInsetsListener());
            contentRoot.RequestApplyInsets();
        }
        else {
            // set window colors such as status bar and navigation bar
            var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor?.ToAndroidColor();
            if (backgroundColor != null) {
                VhUtils.TryInvoke("SetStatusBarColor", () =>
                    window.SetStatusBarColor(backgroundColor.Value));

                VhUtils.TryInvoke("SetNavigationBarColor", () =>
                    window.SetNavigationBarColor(backgroundColor.Value));
            }
        }
    }

    private static void SetBarIconAppearance(Window window, bool lightStatusBars, bool lightNavBars)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return;

        var insetsController = window.DecorView.WindowInsetsController;
        if (insetsController == null) 
            return;

        const int lightStatus = (int)WindowInsetsControllerAppearance.LightStatusBars;
        const int lightNav = (int)WindowInsetsControllerAppearance.LightNavigationBars;

        const int mask = lightStatus | lightNav;
        var appearance = 0;
        if (lightStatusBars) appearance |= lightStatus;
        if (lightNavBars) appearance |= lightNav;
        insetsController.SetSystemBarsAppearance(appearance, mask);
    }

    private class WindowInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
        {
            // Set the background color
            var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor?.ToAndroidColor();
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
                return WindowInsets.Consumed;
            }

            return insets; // Consume the insets
        }
    }
}