using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Extensions;

public static class DisposableExtensions
{
    // for another library's type, whose close may throw; ours never throw from Dispose, so they are disposed plainly
    public static void TryDispose(this IDisposable? disposable, LogLevel failureLevel)
    {
        if (disposable == null)
            return;

        try {
            disposable.Dispose();
        }
        catch (Exception ex) {
            VhInvoker.LogFailure(ex, $"dispose {VhLogger.FormatType(disposable)}", failureLevel);
        }
    }
}
