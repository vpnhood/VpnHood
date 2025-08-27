using Android.Content.Res;
using Android.Util;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.Common.Activities;

internal class AndroidAppLoader
{
    public static void Init(Activity activity)
    {
        try {
            activity.SetContentView(Create(activity));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to initialize loading screen.");
        }
    }

    private static View Create(Activity activity)
    {
        // Build the loading layout inline (no XML resource)
        var metrics = activity.Resources?.DisplayMetrics;
        int DpToPx(int dp) => (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, dp, metrics);

        var linearLayout = new LinearLayout(activity) {
            Orientation = Android.Widget.Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        linearLayout.SetGravity(GravityFlags.CenterHorizontal | GravityFlags.CenterVertical);
        linearLayout.SetPadding(DpToPx(60), DpToPx(0), DpToPx(60), DpToPx(0));

        // set window background color
        var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor?.ToAndroidColor();
        if (backgroundColor != null)
            VhUtils.TryInvoke("linearLayout.SetBackgroundColor", () =>
                linearLayout.SetBackgroundColor(backgroundColor.Value));

        // Create the progress bar
        var progressBar = new ProgressBar(activity, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) {
            Indeterminate = true,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };

        // set progressbar color
        var progressBarColor = VpnHoodApp.Instance.Resources.Colors.ProgressBarColor?.ToAndroidColor();
        if (progressBarColor != null)
            VhUtils.TryInvoke("progressBar.IndeterminateTintList", () =>
                progressBar.IndeterminateTintList = ColorStateList.ValueOf(progressBarColor.Value));

        linearLayout.AddView(progressBar);
        return linearLayout;
    }
}