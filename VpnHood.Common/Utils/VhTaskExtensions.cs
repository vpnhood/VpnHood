using System.Runtime.CompilerServices;

namespace VpnHood.Common.Utils;

public static class VhTaskExtensions
{
    public static ConfiguredTaskAwaitable VhConfigureAwait(this Task task)
    {
        return task.ConfigureAwait(false);
    }

    public static ConfiguredTaskAwaitable<T> VhConfigureAwait<T>(this Task<T> task)
    {
        return task.ConfigureAwait(false);
    }

    public static ConfiguredValueTaskAwaitable VhConfigureAwait(this ValueTask task)
    {
        return task.ConfigureAwait(false);
    }

    public static ConfiguredValueTaskAwaitable<T> VhConfigureAwait<T>(this ValueTask<T> task)
    {
        return task.ConfigureAwait(false);
    }

}