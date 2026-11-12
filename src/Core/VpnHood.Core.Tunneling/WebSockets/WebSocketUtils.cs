// ReSharper disable RedundantCast

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Tunneling.WebSockets;

/// <summary>Provides the parts of the WebSocket protocol the client and the server share.</summary>
public static class WebSocketUtils
{
    internal static void MaskPayload(Span<byte> payload, ReadOnlySpan<byte> key, int offset)
    {
        for (var i = 0; i < payload.Length; i++)
            payload[i] ^= key[(offset + i) & 3];
    }

    /// <summary>
    /// The close status of RFC 6455's registry for a server that cannot serve for now, "Try Again Later": 1013.
    /// </summary>
    public const ushort CloseStatusTryAgainLater = 1013;

    // a server's frame is unmasked
    internal static ReadOnlyMemory<byte> BuildServerCloseFrame(ushort closeStatus)
    {
        var buffer = new byte[4];
        buffer[0] = 0x88; // FIN bit + opcode 8 (Close)
        buffer[1] = 2; // unmasked, length 2
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), closeStatus);
        return buffer;
    }

    internal static Span<byte> BuildWebSocketFrameHeader(
        Span<byte> buffer, long payloadLength, bool closeConnection = false)
    {
        // Always ensure buffer is large enough for worst case (64-bit length)
        if (buffer.Length < 10)
            throw new ArgumentException("Buffer too small, must be at least 10 bytes.", nameof(buffer));

        // FIN bit explicitly set (0x80), OPCODE=8 (close) or 2 (binary)
        var opcode = (byte)(0x80 | (closeConnection ? 0x08 : 0x02));
        buffer[0] = opcode;

        int offset;
        switch (payloadLength) {
            case <= 125:
                buffer[1] = (byte)payloadLength;
                offset = 2;
                break;

            case <= ushort.MaxValue:
                buffer[1] = 126;
                buffer[2] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[3] = (byte)(payloadLength & 0xFF);
                offset = 4;
                break;

            default:
                buffer[1] = 127;
                buffer[2] = (byte)((payloadLength >> 56) & 0xFF);
                buffer[3] = (byte)((payloadLength >> 48) & 0xFF);
                buffer[4] = (byte)((payloadLength >> 40) & 0xFF);
                buffer[5] = (byte)((payloadLength >> 32) & 0xFF);
                buffer[6] = (byte)((payloadLength >> 24) & 0xFF);
                buffer[7] = (byte)((payloadLength >> 16) & 0xFF);
                buffer[8] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[9] = (byte)(payloadLength & 0xFF);
                offset = 10;
                break;
        }

        return buffer[..offset];
    }

    internal static Span<byte> BuildWebSocketFrameHeader(
        Span<byte> buffer, long payloadLength, scoped ReadOnlySpan<byte> maskKey, bool closeConnection = false)
    {
        // Always ensure buffer is large enough for worst case (64-bit length + mask)
        if (buffer.Length < 14)
            throw new ArgumentException("Buffer too small, must be at least 14 bytes.", nameof(buffer));

        if (maskKey.Length != 4)
            throw new ArgumentException("Mask key must be 4 bytes.", nameof(maskKey));

        // FIN bit explicitly set (0x80), OPCODE=8 (close) or 2 (binary)
        var opcode = (byte)(0x80 | (closeConnection ? 0x08 : 0x02));
        buffer[0] = opcode;

        int offset;
        switch (payloadLength) {
            case <= 125:
                buffer[1] = (byte)(0x80 | (byte)payloadLength); // set MASK bit
                offset = 2;
                break;

            case <= ushort.MaxValue:
                buffer[1] = (byte)(0x80 | 126);
                buffer[2] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[3] = (byte)(payloadLength & 0xFF);
                offset = 4;
                break;

            default:
                buffer[1] = (byte)(0x80 | 127);
                buffer[2] = (byte)((payloadLength >> 56) & 0xFF);
                buffer[3] = (byte)((payloadLength >> 48) & 0xFF);
                buffer[4] = (byte)((payloadLength >> 40) & 0xFF);
                buffer[5] = (byte)((payloadLength >> 32) & 0xFF);
                buffer[6] = (byte)((payloadLength >> 24) & 0xFF);
                buffer[7] = (byte)((payloadLength >> 16) & 0xFF);
                buffer[8] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[9] = (byte)(payloadLength & 0xFF);
                offset = 10;
                break;
        }

        // Copy mask key after length fields
        maskKey.CopyTo(buffer.Slice(offset, 4));
        offset += 4;

        return buffer[..offset];
    }

    internal static async Task<WebSocketHeader> ReadWebSocketHeader(Stream stream, Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (buffer.Length < 14)
            throw new ArgumentException("Buffer must be at least 14 bytes.", nameof(buffer));

        // Always read first 2 bytes
        await stream.ReadExactlyAsync(buffer[..2], cancellationToken).Vhc();

        var firstByte = buffer.Span[0];
        var secondByte = buffer.Span[1];

        var opcode = (byte)(firstByte & 0x0F);
        var isMasked = (secondByte & 0x80) != 0;
        var lenIndicator = (byte)(secondByte & 0x7F);

        if ((firstByte & 0x70) != 0 || opcode is not (0 or 1 or 2 or 8 or 9 or 10))
            throw new InvalidDataException("Unsupported WebSocket frame flags or opcode.");
        if (opcode >= 8 && ((firstByte & 0x80) == 0 || lenIndicator > 125))
            throw new InvalidDataException("WebSocket control frames must be final and at most 125 bytes.");

        long payloadLength;
        byte headerLength = 2;
        ReadOnlySpan<byte> span;


        switch (lenIndicator) {
            case <= 125:
                payloadLength = lenIndicator;
                break;

            case 126:
                await stream.ReadExactlyAsync(buffer.Slice(2, 2), cancellationToken).Vhc();
                span = buffer.Span;
                payloadLength = (span[2] << 8) | span[3];
                headerLength += 2;
                break;

            // 127
            default:
                await stream.ReadExactlyAsync(buffer.Slice(2, 8), cancellationToken).Vhc();
                span = buffer.Span;
                payloadLength = ((long)span[2] << 56) |
                                ((long)span[3] << 48) |
                                ((long)span[4] << 40) |
                                ((long)span[5] << 32) |
                                ((long)span[6] << 24) |
                                ((long)span[7] << 16) |
                                ((long)span[8] << 8) |
                                (long)span[9];
                headerLength += 8;
                break;
        }

        if (payloadLength < 0)
            throw new InvalidDataException("Invalid WebSocket payload length.");

        var maskKey = Memory<byte>.Empty;
        if (isMasked) {
            maskKey = buffer.Slice(headerLength, 4);
            await stream.ReadExactlyAsync(maskKey, cancellationToken).Vhc();
            // ReSharper disable once RedundantAssignment
            headerLength += 4;
        }


        var header = new WebSocketHeader {
            IsFinal = (firstByte & 0x80) != 0,
            IsContinuation = opcode == 0,
            IsBinary = opcode == 0x2,
            IsText = opcode == 0x1,
            IsPing = opcode == 0x9,
            IsPong = opcode == 0xA,
            IsCloseConnection = opcode == 0x8,
            PayloadLength = payloadLength,
            MaskKey = maskKey
        };

        return header;
    }


    /// <summary>
    /// Computes the <c>Sec-WebSocket-Accept</c> value of a server's upgrade response, from its request's
    /// <c>Sec-WebSocket-Key</c>.
    /// </summary>
    /// <param name="secWebSocketKey">The request's key.</param>
    /// <returns>The value.</returns>
    public static string ComputeWebSocketAccept(string secWebSocketKey)
    {
        var combined = secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var sha1 = SHA1.HashData(Encoding.ASCII.GetBytes(combined));
        return Convert.ToBase64String(sha1);
    }
}
