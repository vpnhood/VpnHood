// ReSharper disable RedundantCast

using System.Security.Cryptography;
using System.Text;

namespace VpnHood.Core.Tunneling.WebSockets;

public static class WebSocketUtils
{
    // ------------------------ CLIENT -------------------------
    public static void BuildWebSocketFrameHeader(Span<byte> buffer, long payloadLength, Span<byte> maskKey)
    {
        if (buffer.Length != WebSocketHeader.FixHeaderLength)
            throw new ArgumentException($"Buffer must be exactly {WebSocketHeader.FixHeaderLength} bytes.");
        if (maskKey.Length != 4)
            throw new ArgumentException("Mask key must be exactly 4 bytes.");

        // FIN=1, OPCODE=2 (binary), MASK=1
        buffer[0] = 0x82;

        int baseHeaderSize;
        switch (payloadLength) {
            case <= 125:
                buffer[1] = (byte)(0x80 | (byte)payloadLength);
                baseHeaderSize = 2;
                break;

            case <= 65535:
                buffer[1] = (byte)(0x80 | 126);
                buffer[2] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[3] = (byte)(payloadLength & 0xFF);
                baseHeaderSize = 4;
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
                baseHeaderSize = 10;
                break;
        }
        // write mask key (4 bytes)
        buffer.Slice(baseHeaderSize, 4).CopyTo(maskKey);
        // fill remaining header up to fixed length with zeros
        for (var i = baseHeaderSize + 4; i < WebSocketHeader.FixHeaderLength; i++)
            buffer[i] = 0x00;
    }

    // ------------------------ SERVER -------------------------
    public static void BuildWebSocketFrameHeader(Span<byte> buffer, long payloadLength)
    {
        if (buffer.Length != WebSocketHeader.FixHeaderLength)
            throw new ArgumentException($"Buffer must be exactly {WebSocketHeader.FixHeaderLength} bytes.");

        // FIN=1, OPCODE=2 (binary), MASK=0
        buffer[0] = 0x82;

        int baseHeaderSize;
        switch (payloadLength) {
            case <= 125:
                buffer[1] = (byte)payloadLength;
                baseHeaderSize = 2;
                break;

            case <= 65535:
                buffer[1] = 126;
                buffer[2] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[3] = (byte)(payloadLength & 0xFF);
                baseHeaderSize = 4;
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
                baseHeaderSize = 10;
                break;
        }
        // no mask key for server, zero 4 bytes
        for (var i = baseHeaderSize; i < baseHeaderSize + 4; i++)
            buffer[i] = 0x00;
        // fill remainder to fixed length
        for (var i = baseHeaderSize + 4; i < WebSocketHeader.FixHeaderLength; i++)
            buffer[i] = 0x00;
    }

    // ------------------------ PARSER -------------------------
    public static WebSocketHeader ParseWebSocketHeader(Span<byte> headerBuffer)
    {
        if (headerBuffer.Length < WebSocketHeader.FixHeaderLength)
            throw new ArgumentException("Header buffer too small.");

        var second = headerBuffer[1];
        var isMasked = (second & 0x80) != 0;
        var lenIndicator = (byte)(second & 0x7F);

        long payloadLen;
        int offset;
        switch (lenIndicator) {
            case <= 125:
                payloadLen = lenIndicator;
                offset = 2;
                break;

            case 126:
                payloadLen = (headerBuffer[2] << 8) | headerBuffer[3];
                offset = 4;
                break;

            default:
                payloadLen = ((long)headerBuffer[2] << 56) |
                             ((long)headerBuffer[3] << 48) |
                             ((long)headerBuffer[4] << 40) |
                             ((long)headerBuffer[5] << 32) |
                             ((long)headerBuffer[6] << 24) |
                             ((long)headerBuffer[7] << 16) |
                             ((long)headerBuffer[8] << 8) |
                             headerBuffer[9];
                offset = 10;
                break;
        }

        var header = new WebSocketHeader {
            HeaderLength = (byte)(isMasked ? offset + 4 : offset),
            PayloadLength = payloadLen + WebSocketHeader.FixHeaderLength
        };
        if (isMasked)
            headerBuffer.Slice(offset, 4).CopyTo(header.MaskKey.AsSpan());

        return header;
    }

    public static string ComputeWebSocketAccept(string secWebSocketKey)
    {
        var combined = secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var sha1 = SHA1.HashData(Encoding.ASCII.GetBytes(combined));
        return Convert.ToBase64String(sha1);
    }
}
