using Microsoft.Extensions.Logging;
using System.Text;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Utils;

public static class StreamExtensions
{
    public static async Task<string> ReadStringAtMostAsync(this Stream stream, int maxByteCount,
        Encoding encoding, CancellationToken cancellationToken = default)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new InvalidOperationException("Stream must be readable.");
        if (maxByteCount <= 0) return string.Empty;

        var buffer = new byte[maxByteCount];
        var totalRead = 0;

        while (totalRead < maxByteCount) {
            var bytesRead = await stream.ReadAsync(buffer, totalRead, maxByteCount - totalRead, cancellationToken);
            if (bytesRead == 0) break; // EOF
            totalRead += bytesRead;
        }

        return encoding.GetString(buffer, 0, totalRead);
    }

    public static async ValueTask SafeDisposeAsync(this Stream stream)
    {
        try {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to dispose stream asynchronously. Falling back to synchronous dispose.");
            // ReSharper disable once MethodHasAsyncOverload
            stream.Dispose();
        }
    }

}