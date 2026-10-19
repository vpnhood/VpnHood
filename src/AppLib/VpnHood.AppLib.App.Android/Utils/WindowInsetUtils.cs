using Android.Graphics;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Android.Utils;

// The activity's window as every UI on Android has it: edge to edge where the version allows, the
// keyboard's inset kept clear, and the system bars and the ground behind them in the UI's own colour,
// its theme's window background, which its host hands in as it becomes known.
public static class WindowInsetUtils
{
    public static void Configure(Window? window,
        bool lightStatusBars, bool lightNavBars, bool navigationBarContrastEnforced, Task<VhColor?> backgroundColor)
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
            contentRoot.SetOnApplyWindowInsetsListener(new WindowInsetsListener(backgroundColor));
            contentRoot.RequestApplyInsets();
            _ = ReapplyInsetsWhenReady(contentRoot, backgroundColor);
        }
        // set window insets listener for API 30
        else if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
            // Android 11–14: valid here (deprecated only on 35+)
            window.SetDecorFitsSystemWindows(false);
            window.SetStatusBarColor(Color.Transparent);
            window.SetNavigationBarColor(Color.Transparent);

            var contentRoot = window.DecorView;
            contentRoot.SetOnApplyWindowInsetsListener(new WindowInsetsListener(backgroundColor));
            contentRoot.RequestApplyInsets();
            _ = ReapplyInsetsWhenReady(contentRoot, backgroundColor);
        }
        else {
            // the bars are opaque on these versions and painted in the app's colour - once it is
            // final, so they are painted once rather than in a colour that would change
            _ = SetBarColorsWhenReady(window, backgroundColor);
        }
    }

    // The colour may arrive a moment after the window. Until it has, the listener paints no
    // background; then the decor is asked to apply its insets again and it paints the one it now
    // knows - so the colour appears rather than changes. The waits come back to the main looper.
    private static async Task ReapplyInsetsWhenReady(View contentRoot, Task<VhColor?> backgroundColor)
    {
        await backgroundColor.ConfigureAwait(true);
        contentRoot.RequestApplyInsets();
    }

    private static async Task SetBarColorsWhenReady(Window window, Task<VhColor?> backgroundColor)
    {
        var color = (await backgroundColor.ConfigureAwait(true))?.ToAndroidColor();
        if (color == null)
            return;

        VhUtils.TryInvoke("SetStatusBarColor", () => window.SetStatusBarColor(color.Value));
        VhUtils.TryInvoke("SetNavigationBarColor", () => window.SetNavigationBarColor(color.Value));
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

    private class WindowInsetsListener(Task<VhColor?> backgroundColor) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
        {
            // the ground behind the bars, once the colour is known
            var color = backgroundColor.IsCompletedSuccessfully ? backgroundColor.Result?.ToAndroidColor() : null;
            if (color != null)
                v.SetBackgroundColor(color.Value);

            if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
                // the keyboard only: the UI keeps clear of the system bars itself
                var mask = insets.IsVisible(WindowInsets.Type.Ime()) ? WindowInsets.Type.Ime() : 0;
                var rect = insets.GetInsets(mask);
                v.SetPadding(rect.Left, rect.Top, rect.Right, rect.Bottom);
                return WindowInsets.Consumed;
            }

            return insets; // Consume the insets
        }
    }
}