using System.Runtime.CompilerServices;

namespace VpnHood.Core.Toolkit.Utils;

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

    public static Task VhWait(this Task task, CancellationToken cancellationToken)
    {
        return VhWait(task, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public static Task VhWait(this Task task, int timeout, CancellationToken cancellationToken)
    {
        return VhWait(task, TimeSpan.FromMilliseconds(timeout), cancellationToken);
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
        var completedTask = await Task.WhenAny(task, timeoutTask).VhConfigureAwait();

        // check if the task is canceled
        cancellationToken.ThrowIfCancellationRequested();

        // check if the task is timed out
        if (completedTask == timeoutTask)
            throw new TimeoutException();

        return await task;
    }

    public static async Task VhWait(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(task, timeoutTask).VhConfigureAwait();

        // check if the task is canceled
        cancellationToken.ThrowIfCancellationRequested();

        // check if the task is timed out
        if (completedTask == timeoutTask)
            throw new TimeoutException();

        await task;
    }

    public static void VhBlock(this ValueTask task)
    {
        if (!task.IsCompleted)
            task.GetAwaiter().GetResult();
    }
}