using System.Text;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Toolkit.Streams;

public static class StreamExtensions
{
    extension(Stream stream)
    {
        public async Task<string> ReadStringAtMostAsync(int maxByteCount,
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

        // The whole stream in memory, ready to read from its start. It says what it costs, because
        // the cost is the whole thing in memory and the buffer grows by doubling. The stream handed
        // in is left open; the caller disposes both.
        public async Task<MemoryStream> ToMemoryStreamAsync(CancellationToken cancellationToken)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).Vhc();
            memoryStream.Position = 0;
            return memoryStream;
        }

        // A stream that can seek: this one when it already does, its bytes in memory when it cannot
        // - an Android asset, an HTTP body. For a reader that needs more than one pass over what it
        // is given: an archive reads its directory from the end, a picture decoder reads a header
        // and then a body.
        //
        // Only the platforms that cannot seek pay, and they pay the whole thing in memory, so where
        // the source can simply be opened twice, open it twice instead. What comes back is the one
        // thing to dispose: this stream when it is handed straight back, the copy when one was made
        // - the source is closed here in that case.
        public async Task<Stream> ToMemoryStreamIfNotSeekableAsync(CancellationToken cancellationToken)
        {
            if (stream.CanSeek)
                return stream;

            await using (stream)
                return await stream.ToMemoryStreamAsync(cancellationToken).Vhc();
        }
    }
}
