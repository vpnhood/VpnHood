using System.Security.Cryptography;
using System.Text;

namespace VpnHood.Tunneling.Utils;

public static class HttpUtil
{
    public const string HttpRequestKey = "HTTP_REQUEST";

    public static async Task<MemoryStream> ReadHeadersAsync(Stream stream,
        CancellationToken cancellationToken, int maxLength = 8192)
    {
        // read header
        var memStream = new MemoryStream(1024);
        try
        {
            var readBuffer = new byte[1];
            var lfCounter = 0;
            while (lfCounter < 4)
            {
                var bytesRead = await stream.ReadAsync(readBuffer, 0, 1, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    return memStream.Length == 0
                        ? memStream // connection has been closed gracefully before sending anything
                        : throw new Exception("HttpStream has been closed unexpectedly.");

                if (readBuffer[0] == '\r' || readBuffer[0] == '\n')
                    lfCounter++;
                else
                    lfCounter = 0;

                await memStream.WriteAsync(readBuffer, 0, 1, cancellationToken).ConfigureAwait(false);

                if (memStream.Length > maxLength)
                    throw new Exception("HTTP header is too big.");
            }

            memStream.Position = 0;
            return memStream;
        }
        catch
        {
            await memStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }


    public static async Task<Dictionary<string, string>?> ParseHeadersAsync(Stream stream,
        CancellationToken cancellationToken, int maxLength = 8192)
    {

        using var memStream = await ReadHeadersAsync(stream, cancellationToken, maxLength);
        if (memStream.Length == 0)
            return null; // connection has been closed gracefully

        var reader = new StreamReader(memStream, Encoding.UTF8);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Read the header lines until an empty line is encountered
        while (true)
        {
            // ReSharper disable once MethodHasAsyncOverload
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
                break;

            if (headers.Count == 0)
                headers[HttpRequestKey] = line;

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

    public static string GetApiKey(byte[] key, string passCheck)
    {
        // convert password to bytearray
        var passCheckBytes = Encoding.UTF8.GetBytes(passCheck);
        if (passCheckBytes.Length != 16)
            throw new InvalidOperationException("Password must be 16 bytes.");

        // create encryptor
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

        // encrypt password
        var buffer = new byte[16];
        encryptor.TransformBlock(passCheckBytes, 0, passCheckBytes.Length, buffer, 0);

        return Convert.ToBase64String(buffer);
    }
}