using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Extensions;

public static class AsyncDisposableExtensions
{
    // the async close only, even after it failed: a sync one may block, as a QUIC stream's waits for its peer
    public static async ValueTask TryDisposeAsync(this IAsyncDisposable? disposable)
    {
        // Check if the disposable is null for safety
        if (disposable == null)
            return;

        try {
            await disposable.DisposeAsync().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not dispose asynchronously. Type: {Type}", disposable.GetType().Name);
        }
    }
}