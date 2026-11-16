using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Extensions;

/// <summary>
/// Provides a disposal helper for <see cref="IDisposable"/> that logs a failure instead of throwing it.
/// </summary>
public static class DisposableExtensions
{
    /// <summary>
    /// Disposes an object, and logs a failure instead of throwing it; a <see langword="null"/> object is passed over.
    /// </summary>
    /// <remarks>
    /// It is meant for another library's types, whose close may throw.
    /// </remarks>
    /// <param name="disposable">The object, or <see langword="null"/>.</param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
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
