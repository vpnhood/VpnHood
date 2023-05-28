using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Tunneling.Channels;

public static class HttpHeaderReader
{
    public static async Task<Dictionary<string, string>> ReadHeadersAsync(Stream stream, int maxLength = 0xFFFF, 
        CancellationToken cancellationToken = default)
    {
        // read header
        using var memStream = new MemoryStream(8192);
        var readBuffer = new byte[1];
        var lfCounter = 0;
        while (lfCounter < 4)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, 0, 1, cancellationToken);
            if (bytesRead == 0)
                throw new Exception("HTTP Stream closed unexpectedly!");

            if (readBuffer[0] == '\r' || readBuffer[0] == '\n')
            {
                lfCounter++;
                continue;
            }

            lfCounter++;
            await memStream.WriteAsync(readBuffer, 0, 1, cancellationToken);

            if (memStream.Length > maxLength)
                throw new Exception("HTTP header is too big.");
        }

        var reader = new StreamReader(memStream, Encoding.UTF8);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // Read the header lines until an empty line is encountered
        while (true)
        {
            // ReSharper disable once MethodHasAsyncOverload
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
                break;

            // Split the header line into header field and value
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0)
            {
                var headerField = line[..separatorIndex].Trim();
                var headerValue = line[(separatorIndex + 1)..].Trim();
                headers[headerField] = headerValue;
            }
        }

        return headers;
    }
}