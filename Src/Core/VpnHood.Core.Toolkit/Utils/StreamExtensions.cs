using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Utils;

public static class StreamExtensions
{
    public static void ReadExact(this Stream stream, Span<byte> buffer)
    {
        var totalBytesRead = 0;
        while (totalBytesRead != buffer.Length) {
            var bytesRead = stream.Read(buffer[totalBytesRead..]);
            if (bytesRead == 0)
                throw new EndOfStreamException($"Unable to read the required {buffer.Length} bytes from the stream.");

            totalBytesRead += bytesRead;
        }
    }

    public static async ValueTask ReadExactAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length) {
            
            // Read from the stream
            var bytesRead = await stream
                .ReadAsync(buffer[totalBytesRead..], cancellationToken)
                .ConfigureAwait(false);

            // If no more bytes are available, and we haven't read enough, throw an exception
            if (bytesRead == 0)
                throw new EndOfStreamException($"Unable to read the required {buffer.Length} bytes from the stream.");

            totalBytesRead += bytesRead;
        }
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