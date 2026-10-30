namespace VpnHood.Core.Server.Listeners.Http3;

// RFC 9114, sections 6-8. These are application error codes, never QUIC transport errors.
internal static class Http3Protocol
{
    public const long NoError = 0x100;
    public const long ProtocolError = 0x101;
    public const long InternalError = 0x102;
    public const long StreamCreationError = 0x103;
    public const long ClosedCriticalStream = 0x104;
    public const long FrameUnexpected = 0x105;
    public const long FrameError = 0x106;
    public const long ExcessiveLoad = 0x107;
    public const long IdError = 0x108;
    public const long SettingsError = 0x109;
    public const long MissingSettings = 0x10a;
    public const long RequestRejected = 0x10b;
    public const long RequestCancelled = 0x10c;
    public const long RequestIncomplete = 0x10d;
    public const long MessageError = 0x10e;
    public const long QpackEncoderError = 0x201;
    public const long QpackDecoderError = 0x202;
    public const int MaxHeaderBytes = 32768;
    public const int MaxConcurrentStreams = 110;
    public static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(130);
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    // Control stream, SETTINGS, MAX_FIELD_SECTION_SIZE=32768. QPACK capacity and blocked streams
    // retain their default zero, as in the reference server. No dynamic table is retained per peer.
    public static readonly byte[] ControlStream = [0, 4, 5, 6, 0x80, 0, 0x80, 0];

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
