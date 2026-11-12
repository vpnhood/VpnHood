using Java.Util.Concurrent;

namespace VpnHood.Core.Client.Devices.Android.Utils;

/// <summary>
/// Provides a Java <c>CompletableFuture</c> as a task.
/// </summary>
public static class CompletableFutureExtensions
{
    /// <summary>Gets a task that completes when the future does.</summary>
    /// <param name="completableFuture">The future.</param>
    /// <returns>
    /// A task whose result is the future's result's text, as a Java string, or <see langword="null"/>; or that fails with
    /// an exception carrying the Java error's text.
    /// </returns>
    /// <exception cref="Exception">The future completed with an error, whose text is the message.</exception>
    public static Task<Java.Lang.Object?> AsTask(this CompletableFuture completableFuture)
    {
        return new CompletableFutureTask(completableFuture).Task;
    }
}