using System.Text;
using System.Text.Json;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Tunneling;

public static class StreamUtil
{
    public static byte[]? ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        return ReadExact(stream, buffer, 0, buffer.Length) ? buffer : null;
    }

    public static async Task<byte[]?> ReadExactAsync(Stream stream, int count,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        if (!await ReadExactAsync(stream, buffer, 0, buffer.Length, cancellationToken).VhConfigureAwait())
            return null;
        return buffer;
    }

    public static bool ReadExact(Stream stream, byte[] buffer, int startIndex, int count)
    {
        var totalRead = 0;
        while (totalRead != count) {
            var read = stream.Read(buffer, startIndex + totalRead, count - totalRead);
            totalRead += read;
            if (read == 0)
                return false;
        }

        return true;
    }

    public static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int startIndex, int count,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead != count) {
            var read = await stream.ReadAsync(buffer, startIndex + totalRead, count - totalRead,
                cancellationToken).VhConfigureAwait();
            totalRead += read;
            if (read == 0)
                return false;
        }

        return true;
    }

    public static T ReadJson<T>(Stream stream, int maxLength = 0xFFFF)
    {
        // read length
        var buffer = ReadExact(stream, 4) ?? throw new Exception($"Could not read {typeof(T).Name}");

        // check json size
        var jsonSize = BitConverter.ToInt32(buffer);
        if (jsonSize == 0)
            throw new Exception("json length is zero!");
        if (jsonSize > maxLength)
            throw new FormatException(
                $"json length is too big! It should be less than {maxLength} bytes but it was {jsonSize} bytes");

        // read json body...
        buffer = ReadExact(stream, jsonSize);
        if (buffer == null)
            throw new Exception("Could not read Message Length!");

        // serialize the request
        var json = Encoding.UTF8.GetString(buffer);
        var ret = JsonSerializer.Deserialize<T>(json) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    public static async Task<T> ReadJsonAsync<T>(Stream stream, CancellationToken cancellationToken,
        int maxLength = 0xFFFF)
    {
        var message = await ReadMessage(stream, cancellationToken, maxLength).VhConfigureAwait();
        var ret = JsonSerializer.Deserialize<T>(message) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    public static async Task<string> ReadMessage(Stream stream, CancellationToken cancellationToken,
        int maxLength = 0xFFFF)
    {
        // read length
        var buffer = await ReadExactAsync(stream, 4, cancellationToken).VhConfigureAwait()
                     ?? throw new Exception("Could not read message.");

        // check unauthorized exception
        if (Encoding.UTF8.GetString(buffer) == "HTTP") //: HTTP/1.1 401
            throw new UnauthorizedAccessException();

        // check json size
        var messageSize = BitConverter.ToInt32(buffer);
        if (messageSize == 0)
            throw new Exception("json length is zero!");

        if (messageSize > maxLength)
            throw new Exception(
                $"json length is too big! It should be less than {maxLength} bytes but it was {messageSize} bytes");

        // read json body...
        buffer = await ReadExactAsync(stream, messageSize, cancellationToken).VhConfigureAwait();
        if (buffer == null)
            throw new Exception("Could not read Message Length!");

        // serialize the request
        var message = Encoding.UTF8.GetString(buffer);
        return message;
    }

    private static byte[] ObjectToJsonBuffer(object obj)
    {
        var jsonBuffer = JsonSerializer.SerializeToUtf8Bytes(obj);
        var buffer = new byte[4 + jsonBuffer.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(jsonBuffer.Length), 0, buffer, 0, 4);
        Buffer.BlockCopy(jsonBuffer, 0, buffer, 4, jsonBuffer.Length);
        return buffer;
    }

    public static void WriteJson(Stream stream, object obj)
    {
        stream.Write(ObjectToJsonBuffer(obj));
    }

    public static Task WriteJsonAsync(Stream stream, object obj, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(ObjectToJsonBuffer(obj), cancellationToken).AsTask();
    }
}