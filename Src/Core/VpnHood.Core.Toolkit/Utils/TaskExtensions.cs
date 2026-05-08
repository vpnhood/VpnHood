using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Utils;

public static class TaskExtensions
{
    public static bool DefaultContinueOnCapturedContext { get; set; }

    public static ConfiguredTaskAwaitable Vhc(this Task task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static ConfiguredTaskAwaitable<T> Vhc<T>(this Task<T> task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static ConfiguredValueTaskAwaitable Vhc(this ValueTask task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static ConfiguredValueTaskAwaitable<T> Vhc<T>(this ValueTask<T> task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static void VhBlock(this ValueTask task)
    {
        // Some IValueTaskSource-backed ValueTasks do not support GetResult before completion.
        // Use the fast path when already completed; otherwise convert to Task and block on it.
        if (task.IsCompleted)
            task.GetAwaiter().GetResult(); // observe completion / re-throw any exception
        else
            task.AsTask().GetAwaiter().GetResult();
    }

    extension(CancellationTokenSource cancellationTokenSource)
    {
        public void TryCancel()
        {
            try {
                if (!cancellationTokenSource.IsCancellationRequested)
                    cancellationTokenSource.Cancel();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex,
                    "Failed to cancel the CancellationTokenSource. This is not a critical error.");
            }
        }

        public async Task TryCancelAsync()
        {
            try {
                if (!cancellationTokenSource.IsCancellationRequested)
                    await cancellationTokenSource.CancelAsync().Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex,
                    "Failed to cancel the CancellationTokenSource. This is not a critical error.");
            }
        }
    }
}