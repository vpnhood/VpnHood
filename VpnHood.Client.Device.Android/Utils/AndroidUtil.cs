using Android.Content;
using Android.Content.PM;

namespace VpnHood.Client.Device.Droid.Utils;

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
}