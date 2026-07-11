namespace VpnHood.Core.VpnAdapters.IosTun.Utils;

// Converts iOS native callback APIs (completion handlers) into awaitable Tasks.
// On a non-null NSError the Task throws NSErrorException; on success it completes normally.
// When the CancellationToken fires the returned Task transitions to Canceled immediately;
// the native operation itself is NOT cancelled (iOS provides no cancellation path for most
// NE settings calls), the await is simply abandoned.
public static class IosCallbackTask
{
    public static Task WaitAsync(
        Action<Action<NSError?>> nativeCall,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var tcs = new TaskCompletionSource();
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        // CRITICAL: nativeCall must receive a NON-NULL callback; passing null causes
        // EXC_BAD_ACCESS when iOS tries to invoke the block.
        nativeCall(err => {
            if (err != null)
                tcs.TrySetException(new NSErrorException(err));
            else
                tcs.TrySetResult();
        });
        return tcs.Task;
    }
}
