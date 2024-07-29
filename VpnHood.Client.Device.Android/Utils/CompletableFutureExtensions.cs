using Java.Util.Concurrent;

namespace VpnHood.Client.Device.Droid.Utils;

internal static class CompletableFutureExtensions
{
    public static Task<Java.Lang.Object?> AsTask(this CompletableFuture completableFuture)
    {
        return new CompletableFutureTask(completableFuture).Task;
    }
}