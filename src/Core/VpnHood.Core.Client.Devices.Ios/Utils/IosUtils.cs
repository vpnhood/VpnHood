namespace VpnHood.Core.Client.Devices.Ios.Utils;

// iOS counterpart of AndroidUtils. Helpers for marshaling work onto the UI (main) thread.
public static class IosUtils
{
    public static Task RunOnUiThread(Action action)
    {
        // Already on the UI thread: run inline so a synchronous caller (blocking on the returned
        // task) can't deadlock waiting for a main-queue dispatch that will never run.
        if (NSThread.IsMain) {
            try {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex) {
                return Task.FromException(ex);
            }
        }

        // completed on the main thread; the awaiting caller must not resume inline on it
        var taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

        // completed on the main thread; the awaiting caller must not resume inline on it
        var taskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
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
