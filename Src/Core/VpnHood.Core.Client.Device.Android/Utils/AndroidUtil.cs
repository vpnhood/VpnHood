using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Graphics;

namespace VpnHood.Core.Client.Device.Droid.Utils;

public static class AndroidUtil
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
            return Android.Provider.Settings.Secure.GetString(
                context.ContentResolver,
                Android.Provider.Settings.Secure.AndroidId);
        }
        catch (Exception ex) {
            Console.WriteLine($"Could not retrieve android id. Message: {ex}");
            return null;
        }
    }
}