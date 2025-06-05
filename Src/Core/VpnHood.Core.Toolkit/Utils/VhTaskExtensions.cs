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

    public static void VhBlock(this ValueTask task)
    {
        if (!task.IsCompleted)
            task.GetAwaiter().GetResult();
    }
}