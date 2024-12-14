using Java.Util.Concurrent;
using Java.Util.Functions;

namespace VpnHood.Core.Client.Device.Droid.Utils;

public class CompletableFutureTask
{
    private readonly TaskCompletionSource<Java.Lang.Object?> _taskCompletionSource = new ();
    public Task<Java.Lang.Object?> Task => _taskCompletionSource.Task;

    private class BiConsumer(CompletableFutureTask completableFutureTask) : Java.Lang.Object, IBiConsumer
    {
        public void Accept(Java.Lang.Object? result, Java.Lang.Object? ex)
        {
            if (ex != null)
                completableFutureTask._taskCompletionSource.TrySetException(new Exception(ex.ToString()));
            else
                completableFutureTask._taskCompletionSource.TrySetResult(result?.ToString());
        }
    }

    public CompletableFutureTask(CompletableFuture completableFuture)
    {
        completableFuture.WhenComplete(new BiConsumer(this));
    }
}