using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Streams;

/// <summary>
/// Provides stream helpers: reading text, and copying a stream into memory.
/// </summary>
public static class StreamExtensions
{
    /// <summary>Provides stream reading helpers.</summary>
    /// <param name="stream">The stream.</param>
    extension(Stream stream)
    {
        /// <summary>Reads text of at most a number of bytes, up to the end of the stream.</summary>
        /// <param name="maxByteCount">The most bytes to read; zero or less reads nothing.</param>
        /// <param name="encoding">The text's encoding.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task whose result is the text.</returns>
        /// <exception cref="ArgumentNullException">The stream is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The stream cannot read.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        public async Task<string> ReadStringAtMostAsync(int maxByteCount,
            Encoding encoding, CancellationToken cancellationToken)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Stream must be readable.");
            if (maxByteCount <= 0) return string.Empty;

            var buffer = new byte[maxByteCount];
            var totalRead = 0;

            while (totalRead < maxByteCount) {
                var bytesRead = await stream.ReadAsync(buffer, totalRead, maxByteCount - totalRead, cancellationToken).Vhc();
                if (bytesRead == 0) break; // EOF
                totalRead += bytesRead;
            }

            return encoding.GetString(buffer, 0, totalRead);
        }

        /// <summary>
        /// Copies the rest of the stream into memory, ready to read from its start: the whole of it in memory, in a buffer
        /// that grows by doubling. The stream is left open; the caller disposes both.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task whose result is the copy.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        public async Task<MemoryStream> ToMemoryStreamAsync(CancellationToken cancellationToken)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).Vhc();
            memoryStream.Position = 0;
            return memoryStream;
        }

        /// <summary>
        /// Returns a stream that can seek: this one when it does, else a copy of its bytes in memory, for a reader that
        /// needs more than one pass, as an archive that reads its directory from the end.
        /// </summary>
        /// <remarks>
        /// Only a stream that cannot seek, such as an Android asset or an HTTP body, pays, with the whole of it in memory;
        /// where the source can be opened twice, open it twice instead. A stream that can seek comes back as it is, at its
        /// position. Otherwise the rest of it, from its position, is copied into a stream that starts at 0, and it is
        /// disposed here, also when the copy fails or is canceled. The returned stream is the one thing to dispose.
        /// </remarks>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task whose result is the stream to read.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        public async Task<Stream> ToMemoryStreamIfNotSeekableAsync(CancellationToken cancellationToken)
        {
            if (stream.CanSeek)
                return stream;

            await using (stream)
                return await stream.ToMemoryStreamAsync(cancellationToken).Vhc();
        }
    }
}
