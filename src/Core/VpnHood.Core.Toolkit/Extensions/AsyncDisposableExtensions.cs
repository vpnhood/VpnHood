using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Extensions;

public static class AsyncDisposableExtensions
{
    public static async ValueTask SafeDisposeAsync(this IAsyncDisposable? disposable)
    {
        // Check if the disposable is null for safety
        if (disposable == null)
            return;

        // Attempt to dispose asynchronously
        try {
            await disposable.DisposeAsync().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to dispose asynchronously.");
        }

        // If the object also implements IDisposable, attempt to dispose it synchronously as a fallback
        if (disposable is IDisposable syncDisposable) {
            try {
                syncDisposable.Dispose();
            }
            catch (Exception syncEx) {
                VhLogger.Instance.LogDebug(syncEx, "Failed to dispose synchronously after async dispose failure.");
            }
        }
    }
}