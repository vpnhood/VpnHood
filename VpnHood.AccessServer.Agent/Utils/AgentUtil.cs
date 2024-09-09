using System.Text;

namespace VpnHood.AccessServer.Agent.Utils;

public static class AgentUtil
{
    public static int GetBestTcpBufferSize(long? totalMemory)
    {
        if (totalMemory == null)
            return 8192;

        var bufferSize = (long)Math.Round((double)totalMemory / 0x80000000) * 4096;
        bufferSize = Math.Max(bufferSize, 8192);
        bufferSize = Math.Min(bufferSize, 8192); //81920, it looks it doesn't have effect
        return (int)bufferSize;
    }

    public static async Task<byte[]> ReadAtMostAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        // Create a buffer to hold the data, with a length of maxBytes
        var buffer = new byte[maxBytes];

        // Attempt to read up to maxBytes from the stream
        var totalBytesRead = 0;
        while (totalBytesRead < maxBytes) {
            // Read from the stream
            var bytesRead = await stream.ReadAsync(buffer, totalBytesRead, maxBytes - totalBytesRead, cancellationToken);

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

    public static async Task<string> ReadStringAtMostAsync(Stream stream, int maxBytes, Encoding encoding, CancellationToken cancellationToken)
    {
        // Use the existing ReadAtMostAsync to read the byte data
        var buffer = await ReadAtMostAsync(stream, maxBytes, cancellationToken);

        // Convert the byte buffer to a string using the provided encoding
        return encoding.GetString(buffer);
    }
}