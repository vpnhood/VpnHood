using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Provider;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Android.Utils;

public static class AndroidUtils
{
    public static string GetAppName(Context? context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.PackageName);
        ArgumentNullException.ThrowIfNull(context.PackageManager);

        return context.PackageManager.GetApplicationLabel(
            context.PackageManager.GetApplicationInfo(context.PackageName, PackageInfoFlags.MetaData));
    }

    public static Task RunOnUiThread(Activity activity, Action action)
    {
        var taskCompletionSource = new TaskCompletionSource();
        activity.RunOnUiThread(() => {
            try {
                action();
                taskCompletionSource.TrySetResult();
            }
            catch (Exception ex) {
                taskCompletionSource.TrySetException(ex);
            }
        });

        return taskCompletionSource.Task;
    }

    public static Task<T> RunOnUiThread<T>(Activity activity, Func<T> action)
    {
        var taskCompletionSource = new TaskCompletionSource<T>();
        activity.RunOnUiThread(() => {
            try {
                var result = action();
                taskCompletionSource.TrySetResult(result);
            }
            catch (Exception ex) {
                taskCompletionSource.TrySetException(ex);
            }
        });

        return taskCompletionSource.Task;
    }

    public static string? GetDeviceId(Context context)
    {
        try {
            return Settings.Secure.GetString(
                context.ContentResolver,
                Settings.Secure.AndroidId);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not retrieve android id.");
            return null;
        }
    }

    public static void ShowToast(string message)
    {
        var handler = new Handler(Looper.MainLooper!);
        handler.Post(() => {
            try {
                Toast.MakeText(Application.Context, message, ToastLength.Short)?.Show();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error showing a toast");
            }
        });
    }

    public static ApiException JavaExceptionToApiException(Java.Lang.Object error)
    {
        return new ApiException(
            new ApiError {
                TypeFullName = error.Class.TypeName,
                TypeName = error.Class.SimpleName,
                Message = error.ToString()
            });
    }

    public static bool IsTv(Context? context = null)
    {
        var ctx = context ?? Application.Context;

        var uiMode = (UiModeManager?)ctx.GetSystemService(Context.UiModeService);
        if (uiMode?.CurrentModeType is UiMode.TypeTelevision)
            return true;

        var packageManager = ctx.PackageManager;
        return packageManager?.HasSystemFeature(PackageManager.FeatureLeanback) is true;
    }

    // a phone as Chrome tells one, by its "Mobile": narrower than a tablet's 600 dp, and no TV, which
    // the width alone would take for a phone (a 1080p TV is 540 dp)
    public static bool IsPhone(Context? context = null)
    {
        var ctx = context ?? Application.Context;
        return !IsTv(ctx) && GetSmallestScreenWidthDp(ctx) < 600;
    }

    // An activity is measured by the largest window the display gives it, not by its own window: once
    // upright on a folded phone, it is letterboxed when the fold opens into a tablet, and the
    // letterbox is a phone's width, so its own size would keep it upright for good. Any other context,
    // or an Android before 11, has only its configuration.
    private static int? GetSmallestScreenWidthDp(Context context)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) &&
            context is Activity { WindowManager: { } windowManager } &&
            context.Resources?.DisplayMetrics is { } displayMetrics) {
            var bounds = windowManager.MaximumWindowMetrics.Bounds;
            return (int)(Math.Min(bounds.Width(), bounds.Height()) / displayMetrics.Density);
        }

        return context.Resources?.Configuration?.SmallestScreenWidthDp;
    }
}