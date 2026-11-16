using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Extensions;

/// <summary>
/// Provides disposal helpers for <see cref="IAsyncDisposable"/> that log a failure instead of throwing it.
/// </summary>
public static class AsyncDisposableExtensions
{
    /// <summary>
    /// Disposes an object asynchronously, and logs a failure instead of throwing it; a <see langword="null"/> object is
    /// passed over.
    /// </summary>
    /// <remarks>
    /// Only the asynchronous close runs, even after it fails: a synchronous one may block.
    /// </remarks>
    /// <param name="disposable">The object, or <see langword="null"/>.</param>
    /// <param name="failureLevel">
    /// The level a failure is logged at; a cancellation is logged at Debug whatever the level. The default, Debug, suits
    /// the bulk network closes, whose peer is often gone already; a close that happens once per process passes Warning.
    /// </param>
    /// <returns>A task that completes when the close does, failed or not.</returns>
    public static async ValueTask TryDisposeAsync(this IAsyncDisposable? disposable,
        LogLevel failureLevel = LogLevel.Debug)
    {
        // Check if the disposable is null for safety
        if (disposable == null)
            return;

        try {
            await disposable.DisposeAsync().Vhc();
        }
        catch (Exception ex) {
            VhInvoker.LogFailure(ex, $"dispose {disposable.GetType().Name} asynchronously", failureLevel);
        }
    }

    /// <summary>
    /// Disposes an object asynchronously, waiting for it at most a timeout, and logs a failure instead of throwing it;
    /// a <see langword="null"/> object is passed over.
    /// </summary>
    /// <remarks>
    /// Past the timeout, a warning is logged and the close goes on without its caller; a failure it ends with is still
    /// logged. The wait starts once <see cref="IAsyncDisposable.DisposeAsync"/> returns its task, so work it does before
    /// that is not bounded.
    /// </remarks>
    /// <param name="disposable">The object, or <see langword="null"/>.</param>
    /// <param name="timeout">How long to wait for the close.</param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
    /// <returns>A task that completes when the close does, failed or not, or at the timeout.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is negative, other than <see cref="Timeout.InfiniteTimeSpan"/>, or too long.
    /// </exception>
    public static async ValueTask TryDisposeAsync(this IAsyncDisposable? disposable, TimeSpan timeout,
        LogLevel failureLevel = LogLevel.Debug)
    {
        if (disposable == null)
            return;

        try {
            await disposable.TryDisposeAsync(failureLevel).AsTask().WaitAsync(timeout).Vhc();
        }
        catch (TimeoutException) {
            VhLogger.Instance.LogWarning("Could not dispose within {Timeout}; going on without it. Type: {Type}",
                timeout, disposable.GetType().Name);
        }
    }
}