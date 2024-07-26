using Java.Util.Concurrent;

namespace VpnHood.Client.App.Droid.Common.Utils;

public static class CompletableFutureExtensions
{
    public static Task<Java.Lang.Object?> AsTask(this CompletableFuture completableFuture)
    {
        return new CompletableFutureTask(completableFuture).Task;
    }
}