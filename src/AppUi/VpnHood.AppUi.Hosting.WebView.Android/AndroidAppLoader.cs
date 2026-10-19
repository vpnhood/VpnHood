using Android.Content.Res;
using Android.Util;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Common;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using Orientation = Android.Widget.Orientation;

namespace VpnHood.AppUi.Hosting.WebView.Android;

internal static class AndroidAppLoader
{
    public static void Init(Activity activity, ThemeColors colors)
    {
        try {
            activity.SetContentView(Create(activity, colors));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to initialize loading screen.");
        }
    }

    private static View Create(Activity activity, ThemeColors colors)
    {
        // Build the loading layout inline (no XML resource)
        var metrics = activity.Resources?.DisplayMetrics;
        int DpToPx(int dp) => (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, dp, metrics);

        var linearLayout = new LinearLayout(activity) {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        linearLayout.SetGravity(GravityFlags.CenterHorizontal | GravityFlags.CenterVertical);
        linearLayout.SetPadding(DpToPx(60), DpToPx(0), DpToPx(60), DpToPx(0));

        // set window background color
        var backgroundColor = colors.WindowBackgroundColor?.ToAndroidColor();
        if (backgroundColor != null)
            VhInvoker.TryInvoke("set the loader's background color", LogLevel.Debug, () =>
                linearLayout.SetBackgroundColor(backgroundColor.Value));

        // Create the progress bar
        var progressBar = new ProgressBar(activity, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) {
            Indeterminate = true,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };

        // set progressbar color
        var progressBarColor = colors.ProgressBarColor?.ToAndroidColor();
        if (progressBarColor != null)
            VhInvoker.TryInvoke("tint the loader's progress bar", LogLevel.Debug, () =>
                progressBar.IndeterminateTintList = ColorStateList.ValueOf(progressBarColor.Value));

        linearLayout.AddView(progressBar);
        return linearLayout;
    }
}