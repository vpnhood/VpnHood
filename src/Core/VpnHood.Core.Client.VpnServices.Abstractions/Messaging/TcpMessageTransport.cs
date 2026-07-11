using System.Buffers.Binary;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

// Shared low-level helpers for the loopback-TCP message transport (listener + client).
// The transport carries opaque length-framed blobs: [int32 little-endian length][content].
public static class TcpMessageTransport
{
    public const int MaxMessageLength = 0xFFFFFF; // 16 MB

    // The bootstrap file that the listener writes (endpoint + key) and the client reads to
    // discover the loopback endpoint. It lives in the shared VpnService config folder.
    public const string ApiFileName = "vpn.api.json";

    public static string GetApiFilePath(string configFolder)
    {
        return Path.Combine(configFolder, ApiFileName);
    }

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
        await stream.WriteAsync(lengthBuffer, cancellationToken).Vhc();
        await stream.WriteAsync(payload, cancellationToken).Vhc();
        await stream.FlushAsync(cancellationToken).Vhc();
    }

    public static async Task<Memory<byte>> ReadFrameAsync(Stream stream, int maxLength,
        CancellationToken cancellationToken)
    {
        // read length
        var lengthBuffer = new byte[4];
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken).Vhc();
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (length <= 0)
            throw new FormatException($"Invalid message length: {length}.");
        if (length > maxLength)
            throw new FormatException(
                $"Message length is too big! It should be less than {maxLength} bytes but it was {length} bytes.");

        // read body
        var buffer = new byte[length].AsMemory();
        await stream.ReadExactlyAsync(buffer, cancellationToken).Vhc();
        return buffer;
    }
}

// The TCP-internal bootstrap info written by the listener and read by the client.
// It is a transport implementation detail and never crosses the IMessage* abstractions.
public class TcpApiBootstrap
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint ApiEndPoint { get; init; }

    public required byte[] ApiKey { get; init; }
}
