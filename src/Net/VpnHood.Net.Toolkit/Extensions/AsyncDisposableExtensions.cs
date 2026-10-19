using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Extensions;

public static class AsyncDisposableExtensions
{
    // the async close only, even after it failed: a sync one may block, as a QUIC stream's waits for its peer. A
    // failure is logged at Debug by default, as most come from the bulk network closes, whose peer is often gone
    // already; a once-per-process close passes Warning
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

    // past the timeout, the close goes on without its caller, and still logs a failure it ends with
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