using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Streams;

/// <summary>
/// Provides the framing of JSON messages on a stream: a 4-byte little-endian length, then that many bytes of UTF-8 JSON.
/// The readers limit the payload; the writers do not.
/// </summary>
public static class StreamUtils
{
    /// <summary>
    /// The readers' default largest payload, in bytes, after the 4-byte length: 512 KB; a payload of exactly it is accepted.
    /// </summary>
    public const int MaxMessageLength = 1024 * 512;

    /// <summary>Reads a framed JSON message as an object, synchronously.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="stream">The stream.</param>
    /// <param name="maxLength">The largest message accepted, in bytes.</param>
    /// <returns>The object.</returns>
    /// <exception cref="FormatException">The length is over <paramref name="maxLength"/>.</exception>
    /// <exception cref="Exception">The length is zero, or the deserialization returns <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    /// <exception cref="JsonException">The JSON is not of the type.</exception>
    public static T ReadObject<T>(Stream stream, int maxLength = MaxMessageLength)
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
                $"json length for {typeof(T)} is too big! It should be less than {maxLength} bytes but it was {jsonSize} bytes");

        // read json body...
        var buffer = new byte[jsonSize].AsMemory();
        stream.ReadExactly(buffer.Span);

        // serialize the request
        var ret = JsonSerializer.Deserialize<T>(buffer.Span) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    /// <summary>Reads a framed JSON message as an object, of at most <see cref="MaxMessageLength"/>.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object.</returns>
    /// <exception cref="FormatException">The length is over the largest.</exception>
    /// <exception cref="Exception">The length is zero, or the deserialization returns <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    /// <exception cref="JsonException">The JSON is not of the type.</exception>
    /// <exception cref="UnauthorizedAccessException">The stream's next four bytes are <c>HTTP</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<T> ReadObjectAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        return ReadObjectAsync<T>(stream, MaxMessageLength, cancellationToken);
    }

    /// <summary>Reads a framed JSON message as an object.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="stream">The stream.</param>
    /// <param name="maxLength">The largest message accepted, in bytes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object.</returns>
    /// <exception cref="FormatException">The length is over <paramref name="maxLength"/>.</exception>
    /// <exception cref="Exception">The length is zero, or the deserialization returns <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    /// <exception cref="JsonException">The JSON is not of the type.</exception>
    /// <exception cref="UnauthorizedAccessException">The stream's next four bytes are <c>HTTP</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<T> ReadObjectAsync<T>(Stream stream, int maxLength, CancellationToken cancellationToken)
    {
        var message = await ReadMessageAsync(stream, maxLength, cancellationToken).Vhc();
        var ret = JsonSerializer.Deserialize<T>(message) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    /// <summary>Reads a framed JSON message as an object with its metadata, of at most <see cref="MaxMessageLength"/>.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="stream">The stream.</param>
    /// <param name="jsonTypeInfo">The type's metadata.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object.</returns>
    /// <exception cref="FormatException">The length is over the largest.</exception>
    /// <exception cref="Exception">The length is zero, or the deserialization returns <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    /// <exception cref="JsonException">The JSON is not of the type.</exception>
    /// <exception cref="UnauthorizedAccessException">The stream's next four bytes are <c>HTTP</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<T> ReadObjectAsync<T>(Stream stream, JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        return ReadObjectAsync(stream, jsonTypeInfo, MaxMessageLength, cancellationToken);
    }

    /// <summary>Reads a framed JSON message as an object with its metadata.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="stream">The stream.</param>
    /// <param name="jsonTypeInfo">The type's metadata.</param>
    /// <param name="maxLength">The largest message accepted, in bytes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object.</returns>
    /// <exception cref="FormatException">The length is over <paramref name="maxLength"/>.</exception>
    /// <exception cref="Exception">The length is zero, or the deserialization returns <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    /// <exception cref="JsonException">The JSON is not of the type.</exception>
    /// <exception cref="UnauthorizedAccessException">The stream's next four bytes are <c>HTTP</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<T> ReadObjectAsync<T>(Stream stream, JsonTypeInfo<T> jsonTypeInfo, int maxLength,
        CancellationToken cancellationToken)
    {
        var message = await ReadMessageAsync(stream, maxLength, cancellationToken).Vhc();
        var ret = JsonSerializer.Deserialize(message, jsonTypeInfo) ?? throw new Exception("Could not read Message!");
        return ret;
    }

    /// <summary>Reads a framed message's text, of at most <see cref="MaxMessageLength"/>.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the text.</returns>
    /// <exception cref="FormatException">The length is over the largest.</exception>
    /// <exception cref="Exception">The length is zero.</exception>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    /// <exception cref="UnauthorizedAccessException">The stream's next four bytes are <c>HTTP</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        return ReadMessageAsync(stream, MaxMessageLength, cancellationToken);
    }

    /// <summary>
    /// Reads a framed message's text; it rejects a frame whose next four bytes are <c>HTTP</c>.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="maxLength">The largest message accepted, in bytes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the text.</returns>
    /// <exception cref="FormatException">The length is over <paramref name="maxLength"/>.</exception>
    /// <exception cref="Exception">The length is zero.</exception>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    /// <exception cref="UnauthorizedAccessException">The stream's next four bytes are <c>HTTP</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<string> ReadMessageAsync(Stream stream, int maxLength, CancellationToken cancellationToken)
    {
        // read length
        var lengthBuffer = new byte[4].AsMemory();
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken).Vhc();

        // check unauthorized exception
        if (lengthBuffer.Span.SequenceEqual("HTTP"u8))
            throw new UnauthorizedAccessException("Stream returned an HTTP response.");

        // check json size
        var messageSize = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer.Span);
        if (messageSize == 0)
            throw new Exception("json length is zero!");

        if (messageSize > maxLength)
            throw new FormatException(
                $"json length is too big! It should be less than {maxLength} bytes but it was {messageSize} bytes");

        // read json body...
        var buffer = new byte[messageSize].AsMemory();
        await stream.ReadExactlyAsync(buffer, cancellationToken).Vhc();

        // serialize the request
        var message = Encoding.UTF8.GetString(buffer.Span);
        return message;
    }

    /// <summary>Frames an object as a JSON message, by its runtime type.</summary>
    /// <param name="obj">The object.</param>
    /// <returns>The frame: the length, then the JSON.</returns>
    public static Memory<byte> ObjectToJsonBuffer(object obj)
    {
        var jsonBuffer = JsonSerializer.SerializeToUtf8Bytes(obj);
        Memory<byte> buffer = new byte[4 + jsonBuffer.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Span[..4], jsonBuffer.Length);
        jsonBuffer.CopyTo(buffer[4..]);
        return buffer;
    }

    /// <summary>Frames an object as a JSON message, with its metadata.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="obj">The object.</param>
    /// <param name="jsonTypeInfo">The type's metadata.</param>
    /// <returns>The frame: the length, then the JSON.</returns>
    public static Memory<byte> ObjectToJsonBuffer<T>(T obj, JsonTypeInfo<T> jsonTypeInfo)
    {
        var jsonBuffer = JsonSerializer.SerializeToUtf8Bytes(obj, jsonTypeInfo);
        Memory<byte> buffer = new byte[4 + jsonBuffer.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Span[..4], jsonBuffer.Length);
        jsonBuffer.CopyTo(buffer[4..]);
        return buffer;
    }

    /// <summary>Frames an object as a JSON message, with its type's metadata.</summary>
    /// <param name="obj">The object.</param>
    /// <param name="jsonTypeInfo">The type's metadata.</param>
    /// <returns>The frame: the length, then the JSON.</returns>
    public static Memory<byte> ObjectToJsonBuffer(object obj, JsonTypeInfo jsonTypeInfo)
    {
        var jsonBuffer = JsonSerializer.SerializeToUtf8Bytes(obj, jsonTypeInfo);
        Memory<byte> buffer = new byte[4 + jsonBuffer.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Span[..4], jsonBuffer.Length);
        jsonBuffer.CopyTo(buffer[4..]);
        return buffer;
    }

    /// <summary>Writes an object as a framed JSON message, synchronously.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="obj">The object.</param>
    public static void WriteObject(Stream stream, object obj)
    {
        stream.Write(ObjectToJsonBuffer(obj).Span);
    }

    /// <summary>Writes an object as a framed JSON message, by its runtime type.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="obj">The object.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static ValueTask WriteObjectAsync(Stream stream, object obj, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(ObjectToJsonBuffer(obj), cancellationToken);
    }

    /// <summary>Writes an object as a framed JSON message, with its metadata.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="stream">The stream.</param>
    /// <param name="obj">The object.</param>
    /// <param name="jsonTypeInfo">The type's metadata.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static ValueTask WriteObjectAsync<T>(Stream stream, T obj, JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        return stream.WriteAsync(ObjectToJsonBuffer(obj, jsonTypeInfo), cancellationToken);
    }

    /// <summary>Writes an object as a framed JSON message, with its type's metadata.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="obj">The object.</param>
    /// <param name="jsonTypeInfo">The type's metadata.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static ValueTask WriteObjectAsync(Stream stream, object obj, JsonTypeInfo jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        return stream.WriteAsync(ObjectToJsonBuffer(obj, jsonTypeInfo), cancellationToken);
    }
}