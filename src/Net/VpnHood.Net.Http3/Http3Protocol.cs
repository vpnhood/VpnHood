namespace VpnHood.Net.Http3;

// RFC 9114, sections 6-8. These are application error codes, never QUIC transport errors.
internal static class Http3Protocol
{
    public const long NoError = Http3ErrorCode.NoError;
    public const long ProtocolError = 0x101;
    public const long InternalError = Http3ErrorCode.InternalError;
    public const long StreamCreationError = 0x103;
    public const long ClosedCriticalStream = 0x104;
    public const long FrameUnexpected = 0x105;
    public const long FrameError = 0x106;
    public const long ExcessiveLoad = Http3ErrorCode.ExcessiveLoad;
    public const long IdError = 0x108;
    public const long SettingsError = 0x109;
    public const long MissingSettings = 0x10a;
    public const long RequestRejected = 0x10b;
    public const long RequestCancelled = 0x10c;
    public const long RequestIncomplete = 0x10d;
    public const long MessageError = 0x10e;
    public const long QpackEncoderError = 0x201;
    public const long QpackDecoderError = 0x202;
    public static byte[] CreateControlStream(int maxHeaderBytes)
    {
        // QPACK capacity and blocked streams retain zero; no dynamic table is kept per peer.
        using var settings = new MemoryStream();
        WriteInteger(settings, 6);
        WriteInteger(settings, maxHeaderBytes);
        using var output = new MemoryStream();
        WriteInteger(output, 0);
        output.Write(Frame(4, settings.GetBuffer().AsSpan(0, (int)settings.Length)));
        return output.ToArray();
    }

    public static byte[] Frame(long type, ReadOnlySpan<byte> payload)
    {
        using var output = new MemoryStream();
        WriteInteger(output, type);
        WriteInteger(output, payload.Length);
        output.Write(payload);
        return output.ToArray();
    }

    public static void WriteInteger(Stream output, long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, (1L << 62) - 1);
        var length = value < 64 ? 1 : value < 16384 ? 2 : value < 1073741824 ? 4 : 8;
        Span<byte> bytes = stackalloc byte[8];
        for (var i = length - 1; i >= 0; i--) {
            bytes[i] = (byte)value;
            value >>= 8;
        }
        bytes[0] |= length switch { 2 => 0x40, 4 => 0x80, 8 => 0xc0, _ => (byte)0 };
        output.Write(bytes[..length]);
    }

    public static long ReadInteger(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset == bytes.Length)
            throw new Http3ProtocolException(FrameError);
        var length = 1 << (bytes[offset] >> 6);
        if (bytes.Length - offset < length)
            throw new Http3ProtocolException(FrameError);
        long value = bytes[offset++] & 0x3f;
        for (var i = 1; i < length; i++)
            value = (value << 8) | bytes[offset++];
        return value;
    }
}
