namespace VpnHood.Core.Client.Devices.Ios.Utils;

/// <summary>
/// Provides the running of work on the UI (main) thread: the iOS counterpart of <c>AndroidUtils</c>.
/// </summary>
public static class IosUtils
{
    /// <summary>Runs an action on the UI thread: at once where the caller is on it, or else queued there.</summary>
    /// <param name="action">The action.</param>
    /// <returns>A task that completes when the action has run, or fails with its exception.</returns>
    /// <remarks>
    /// Where the action is queued, its task completes with continuations run asynchronously, not inline on the UI
    /// thread; where it runs at once, the task is already complete.
    /// </remarks>
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

    /// <summary>Runs a function on the UI thread: at once where the caller is on it, or else queued there.</summary>
    /// <typeparam name="T">The type of the function's result.</typeparam>
    /// <param name="action">The function.</param>
    /// <returns>A task whose result is the function's, or that fails with its exception.</returns>
    /// <remarks>
    /// Where the function is queued, its task completes with continuations run asynchronously, not inline on the UI
    /// thread; where it runs at once, the task is already complete.
    /// </remarks>
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
