using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace VpnHood.Core.Toolkit.Utils;

public static class StreamUtils
{
    public static T ReadObject<T>(Stream stream, int maxLength = 0xFFFF)
    {
        // read length
        Span<byte> lengthBuffer = stackalloc byte[4];
        stream.ReadExactly(lengthBuffer);

        // check json size
        var jsonSize = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (jsonSize == 0)
            throw new Exception("json length is zero!");

        if (jsonSize > maxLength)
            throw new FormatException(
                $"json length is too big! It should be less than {maxLength} bytes but it was {jsonSize} bytes");

        // read json body...
        var buffer = new byte[jsonSize].AsMemory();
        stream.ReadExactly(buffer.Span);

        // serialize the request
        var ret = JsonSerializer.Deserialize<T>(buffer.Span) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    public static async Task<T> ReadObjectAsync<T>(Stream stream, CancellationToken cancellationToken,
        int maxLength = 0xFFFF)
    {
        var message = await ReadMessageAsync(stream, cancellationToken, maxLength).Vhc();
        var ret = JsonSerializer.Deserialize<T>(message) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    public static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken,
        int maxLength = 0xFFFF)
    {
        // read length
        var lengthBuffer = new byte[4].AsMemory();
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
        
        // check unauthorized exception
        if (lengthBuffer.Span.SequenceEqual("HTTP"u8))
            throw new UnauthorizedAccessException("Stream returned an HTTP response.");

        // check json size
        var messageSize = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer.Span);
        if (messageSize == 0)
            throw new Exception("json length is zero!");

        if (messageSize > maxLength)
            throw new Exception(
                $"json length is too big! It should be less than {maxLength} bytes but it was {messageSize} bytes");

        // read json body...
        var buffer = new byte[messageSize].AsMemory();
        await stream.ReadExactlyAsync(buffer, cancellationToken).Vhc();

        // serialize the request
        var message = Encoding.UTF8.GetString(buffer.Span);
        return message;
    }

    private static Memory<byte> ObjectToJsonBuffer(object obj)
    {
        var jsonBuffer = JsonSerializer.SerializeToUtf8Bytes(obj);
        Memory<byte> buffer = new byte[4 + jsonBuffer.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Span[..4], jsonBuffer.Length);
        jsonBuffer.CopyTo(buffer[4..]);
        return buffer;
    }

    public static void WriteObject(Stream stream, object obj)
    {
        stream.Write(ObjectToJsonBuffer(obj).Span);
    }

    public static ValueTask WriteObjectAsync(Stream stream, object obj, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(ObjectToJsonBuffer(obj), cancellationToken);
    }
}
