using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Provider;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Android.Utils;

/// <summary>
/// Provides Android helpers: the app's name, the UI thread, the device's ID, toasts, and what kind of device it is.
/// </summary>
public static class AndroidUtils
{
    /// <summary>Gets the app's name, as Android labels it.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/>, its package name or its package manager is <see langword="null"/>.
    /// </exception>
    public static string GetAppName(Context? context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.PackageName);
        ArgumentNullException.ThrowIfNull(context.PackageManager);

        return context.PackageManager.GetApplicationLabel(
            context.PackageManager.GetApplicationInfo(context.PackageName, PackageInfoFlags.MetaData));
    }

    /// <summary>Runs an action on the activity's UI thread: at once where the caller is on it, or else queued there.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="action">The action.</param>
    /// <returns>A task that completes when the action has run, or fails with its exception.</returns>
    /// <remarks>An awaiting caller may resume on the UI thread.</remarks>
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

    /// <summary>Runs a function on the activity's UI thread: at once where the caller is on it, or else queued there.</summary>
    /// <typeparam name="T">The type of the function's result.</typeparam>
    /// <param name="activity">The activity.</param>
    /// <param name="action">The function.</param>
    /// <returns>A task whose result is the function's, or that fails with its exception.</returns>
    /// <remarks>An awaiting caller may resume on the UI thread.</remarks>
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

    /// <summary>
    /// Gets the device's Android ID: from Android 8, one for each app signing key, user and device.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>
    /// The ID, or <see langword="null"/> where Android gives none or it cannot be read, which is logged.
    /// </returns>
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

    /// <summary>Shows a short toast, from any thread; one that cannot be shown is logged.</summary>
    /// <param name="message">The message.</param>
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

    /// <summary>Makes an <see cref="ApiException"/> of a Java error, with its Java type's names and its text.</summary>
    /// <param name="error">The Java error.</param>
    /// <returns>The exception.</returns>
    internal static ApiException JavaExceptionToApiException(Java.Lang.Object error)
    {
        return new ApiException(
            new ApiError {
                TypeFullName = error.Class.TypeName,
                TypeName = error.Class.SimpleName,
                Message = error.ToString()
            });
    }

    /// <summary>Determines whether the device is a TV: in TV mode, or with the leanback feature.</summary>
    /// <param name="context">The context, or <see langword="null"/> for the application's.</param>
    /// <returns><see langword="true"/> if the device is a TV; otherwise, <see langword="false"/>.</returns>
    public static bool IsTv(Context? context = null)
    {
        var ctx = context ?? Application.Context;

        var uiMode = (UiModeManager?)ctx.GetSystemService(Context.UiModeService);
        if (uiMode?.CurrentModeType is UiMode.TypeTelevision)
            return true;

        var packageManager = ctx.PackageManager;
        return packageManager?.HasSystemFeature(PackageManager.FeatureLeanback) is true;
    }

    /// <summary>
    /// Determines whether the device is a phone, as Chrome tells one by its "Mobile": narrower than a tablet's 600 dp,
    /// and no TV, which the width alone would take for a phone (a 1080p TV is 540 dp).
    /// </summary>
    /// <param name="context">
    /// The context, or <see langword="null"/> for the application's; an activity is measured by the largest window the
    /// display gives it, from Android 11.
    /// </param>
    /// <returns><see langword="true"/> if the device is a phone; otherwise, <see langword="false"/>.</returns>
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