using System.Runtime.CompilerServices;

namespace VpnHood.Core.Common.Utils;

public static class VhTaskExtensions
{
    public static bool DefaultContinueOnCapturedContext { get; set; }

    public static ConfiguredTaskAwaitable VhConfigureAwait(this Task task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static ConfiguredTaskAwaitable<T> VhConfigureAwait<T>(this Task<T> task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static ConfiguredValueTaskAwaitable VhConfigureAwait(this ValueTask task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static ConfiguredValueTaskAwaitable<T> VhConfigureAwait<T>(this ValueTask<T> task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static Task<T> VhWait<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        return VhWait(task, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public static async Task<T> VhWait<T>(this Task<T> task, int timeout, CancellationToken cancellationToken)
    {
        return await VhWait(task, TimeSpan.FromMilliseconds(timeout), cancellationToken);
    }

    public static async Task<T> VhWait<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        await Task.WhenAny(task, timeoutTask).VhConfigureAwait();

        // check if the task is canceled
        cancellationToken.ThrowIfCancellationRequested();

        // check if the task is timed out
        if (timeoutTask.IsCompleted)
            throw new TimeoutException();

        return await task;
    }

}