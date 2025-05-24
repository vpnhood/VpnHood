using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace VpnHood.Core.Toolkit.Utils;

public static class StreamUtils
{
    public static Memory<byte> ReadExact(Stream stream, int count)
    {
        Memory<byte> buffer = new byte[count];
        ReadExact(stream, buffer.Span);
        return buffer;
    }

    public static async Task<Memory<byte>> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        Memory<byte> buffer = new byte[count];
        await ReadExactAsync(stream, buffer, cancellationToken).VhConfigureAwait();
        return buffer;
    }

    public static void ReadExact(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead != buffer.Length) {
            var read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                throw new EndOfStreamException();

            totalRead += read;
        }
    }

    public static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead != buffer.Length) {
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken).VhConfigureAwait();
            // check end of stream
            if (read == 0)
                throw new EndOfStreamException();

            // set total read bytes
            totalRead += read;
        }
    }

    public static T ReadObject<T>(Stream stream, int maxLength = 0xFFFF)
    {
        // read length
        var buffer = ReadExact(stream, 4);

        // check json size
        var jsonSize = BinaryPrimitives.ReadInt32LittleEndian(buffer.Span);
        if (jsonSize == 0)
            throw new Exception("json length is zero!");

        if (jsonSize > maxLength)
            throw new FormatException(
                $"json length is too big! It should be less than {maxLength} bytes but it was {jsonSize} bytes");

        // read json body...
        buffer = ReadExact(stream, jsonSize);

        // serialize the request
        var ret = JsonSerializer.Deserialize<T>(buffer.Span) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    public static async Task<T> ReadObjectAsync<T>(Stream stream, CancellationToken cancellationToken,
        int maxLength = 0xFFFF)
    {
        var message = await ReadMessageAsync(stream, cancellationToken, maxLength).VhConfigureAwait();
        var ret = JsonSerializer.Deserialize<T>(message) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    public static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken,
        int maxLength = 0xFFFF)
    {
        // read length
        var buffer = await ReadExactAsync(stream, 4, cancellationToken).VhConfigureAwait();

        // check unauthorized exception
        if (buffer.Span.SequenceEqual("HTTP"u8))
            throw new UnauthorizedAccessException("Stream returned an HTTP response.");

        // check json size
        var messageSize = BinaryPrimitives.ReadInt32LittleEndian(buffer.Span);
        if (messageSize == 0)
            throw new Exception("json length is zero!");

        if (messageSize > maxLength)
            throw new Exception(
                $"json length is too big! It should be less than {maxLength} bytes but it was {messageSize} bytes");

        // read json body...
        buffer = await ReadExactAsync(stream, messageSize, cancellationToken).VhConfigureAwait();

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