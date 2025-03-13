using System.Text;

namespace VpnHood.Core.Toolkit.Utils;

public static class StreamExtensions
{
    public static async Task<byte[]> ReadAtMostAsync(this Stream stream, int maxBytes,
        CancellationToken cancellationToken)
    {
        // Create a buffer to hold the data, with a length of maxBytes
        var buffer = new byte[maxBytes];

        // Attempt to read up to maxBytes from the stream
        var totalBytesRead = 0;
        while (totalBytesRead < maxBytes) {
            // Read from the stream
            var bytesRead =
                await stream.ReadAsync(buffer, totalBytesRead, maxBytes - totalBytesRead, cancellationToken);

            // If no more bytes are available, stop reading
            if (bytesRead == 0)
                break;

            totalBytesRead += bytesRead;
        }

        // If the total bytes read exceeds maxBytes, throw an exception
        if (totalBytesRead > maxBytes)
            throw new InvalidOperationException("Exceeded the maximum allowed bytes to read.");


        // Use the range indexer to return only the portion of the buffer that contains data
        return buffer[..totalBytesRead];
    }

    public static async Task<string> ReadStringAtMostAsync(this Stream stream, int maxBytes, Encoding encoding,
        CancellationToken cancellationToken)
    {
        // Use the existing ReadAtMostAsync to read the byte data
        var buffer = await ReadAtMostAsync(stream, maxBytes, cancellationToken);

        // Convert the byte buffer to a string using the provided encoding
        return encoding.GetString(buffer);
    }
}