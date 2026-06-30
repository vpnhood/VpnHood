using Foundation;
using UIKit;

namespace VpnHood.Core.Client.Devices.Ios.Utils;

// iOS counterpart of AndroidUtils. Helpers for marshaling work onto the UI (main) thread.
public static class IosUtils
{
    public static Task RunOnUiThread(Action action)
    {
        // Already on the UI thread: run inline so a synchronous caller (blocking on the returned
        // task) can't dead-lock waiting for a main-queue dispatch that will never run.
        if (NSThread.IsMain) {
            try {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex) {
                return Task.FromException(ex);
            }
        }

        var taskCompletionSource = new TaskCompletionSource();
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => {
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

    public static Task<T> RunOnUiThread<T>(Func<T> action)
    {
        // Already on the UI thread: run inline (see RunOnUiThread(Action) above).
        if (NSThread.IsMain) {
            try {
                return Task.FromResult(action());
            }
            catch (Exception ex) {
                return Task.FromException<T>(ex);
            }
        }

        var taskCompletionSource = new TaskCompletionSource<T>();
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => {
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
}
