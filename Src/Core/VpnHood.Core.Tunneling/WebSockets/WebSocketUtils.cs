// ReSharper disable RedundantCast
namespace VpnHood.Core.Tunneling.WebSockets;

public class WebSocketUtils
{
    public static int BuildWebSocketFrameHeader(byte[] buffer, long payloadLength, byte[] maskKey)
    {
        if (buffer.Length < 14)
            throw new ArgumentException("Buffer must be at least 14 bytes.");

        if (maskKey.Length != 4)
            throw new ArgumentException("Mask key must be exactly 4 bytes.");

        // Always FIN=1 (single frame), OPCODE=2 (binary)
        buffer[0] = 0x82;

        int headerLength;
        switch (payloadLength)
        {
            case <= 125:
                buffer[1] = (byte)(0x80 | (byte)payloadLength); // MASK=1
                headerLength = 2 + 4; // header + mask
                break;

            case <= 65535:
                buffer[1] = (byte)(0x80 | 126); // MASK=1 + 126
                buffer[2] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[3] = (byte)(payloadLength & 0xFF);
                headerLength = 4 + 4;
                break;

            default:
                buffer[1] = (byte)(0x80 | 127); // MASK=1 + 127
                buffer[2] = (byte)((payloadLength >> 56) & 0xFF);
                buffer[3] = (byte)((payloadLength >> 48) & 0xFF);
                buffer[4] = (byte)((payloadLength >> 40) & 0xFF);
                buffer[5] = (byte)((payloadLength >> 32) & 0xFF);
                buffer[6] = (byte)((payloadLength >> 24) & 0xFF);
                buffer[7] = (byte)((payloadLength >> 16) & 0xFF);
                buffer[8] = (byte)((payloadLength >> 8) & 0xFF);
                buffer[9] = (byte)(payloadLength & 0xFF);
                headerLength = 10 + 4;
                break;
        }

        // Write the mask key after the header length section
        var maskOffset = headerLength - 4;
        buffer[maskOffset] = maskKey[0];
        buffer[maskOffset + 1] = maskKey[1];
        buffer[maskOffset + 2] = maskKey[2];
        buffer[maskOffset + 3] = maskKey[3];

        return headerLength;
    }

    public static WebSocketHeader ParseWebSocketHeader(byte[] headerBuffer)
    {
        if (headerBuffer.Length < 2)
            throw new ArgumentException("Header buffer too small.");

        //var firstByte = headerBuffer[0];
        var secondByte = headerBuffer[1];

        var isMasked = (secondByte & 0x80) != 0;
        if (!isMasked)
            throw new InvalidOperationException("Client frames must be masked (MASK bit not set).");

        long payloadLen;
        int offset;
        var lenIndicator = (byte)(secondByte & 0x7F);

        switch (lenIndicator)
        {
            case <= 125:
                payloadLen = lenIndicator;
                offset = 2;
                break;

            case 126:
                payloadLen = (headerBuffer[2] << 8) | headerBuffer[3];
                offset = 4;
                break;

            case 127:
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

            default:
                throw new InvalidOperationException("Invalid payload length indicator.");
        }

        var maskKey = new byte[4];
        Buffer.BlockCopy(headerBuffer, offset, maskKey, 0, 4);

        return new WebSocketHeader {
            HeaderLength = offset + 4,
            PayloadLength = payloadLen,
            MaskKey = maskKey
        };
    }
}
