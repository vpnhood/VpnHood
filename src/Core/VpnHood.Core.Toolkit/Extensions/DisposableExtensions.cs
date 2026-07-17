using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Extensions;

public static class DisposableExtensions
{
    public static void SafeDispose(this IDisposable disposable)
    {
        // Check if the disposable is null for safety
        if (disposable == null!)
            return;

        try {
            disposable.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to dispose. ObjectType: {ObjectType}",
                VhLogger.FormatType(disposable));
        }
    }
}
