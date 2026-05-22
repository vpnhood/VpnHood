using System.Text;

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

    }

}