using System.Buffers;
using System.Globalization;
using System.Text;

namespace VpnHood.Net.Http3;

// RFC 9204 field sections with no dynamic table. The connection advertises zero table capacity and
// zero blocked streams, so a dynamic reference is a decoding error, never a reason to retain a block.
// This class decodes compression only; the HTTP/3 request parser validates header semantics.
internal static partial class Http3Qpack
{
    private const ulong MaxInteger = (1UL << 62) - 1;

    public static byte[] EncodeResponse(int statusCode, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (statusCode is < 100 or > 999)
            throw new ArgumentOutOfRangeException(nameof(statusCode));

        var writer = new ArrayBufferWriter<byte>(128);
        WriteByte(writer, 0); // Required Insert Count.
        WriteByte(writer, 0); // Base.
        var statusIndex = statusCode switch {
            100 => 63, 103 => 24, 200 => 25, 204 => 64, 206 => 65, 302 => 66, 304 => 26,
            400 => 67, 403 => 68, 404 => 27, 421 => 69, 425 => 70, 500 => 71, 503 => 28,
            _ => -1
        };
        if (statusIndex >= 0)
            WriteInteger(writer, 0xc0, 6, statusIndex);
        else
            // The reference uses the :status name at index 63 for a status absent from the table.
            WriteNameReference(writer, 63, statusCode.ToString(CultureInfo.InvariantCulture));
        foreach (var (name, value) in headers) {
            if (name == "content-length" && value == "0")
                WriteInteger(writer, 0xc0, 6, 4);
            else if (name is "date" or "server" or "content-length")
                WriteNameReference(writer, name switch { "date" => 6, "server" => 92, _ => 4 }, value);
            else
                WriteLiteral(writer, name, value);
        }
        return writer.WrittenSpan.ToArray();
    }

    private static Http3QpackFieldKind GetFieldKind(byte first)
    {
        if ((first & 0x80) != 0) {
            if ((first & 0x40) == 0)
                throw new Http3QpackException("A field line references the disabled dynamic table.");
            return Http3QpackFieldKind.Indexed;
        }
        if ((first & 0x40) != 0) {
            if ((first & 0x10) == 0)
                throw new Http3QpackException("A field name references the disabled dynamic table.");
            return Http3QpackFieldKind.NameReference;
        }
        if ((first & 0x20) != 0)
            return Http3QpackFieldKind.Literal;
        throw new Http3QpackException("A field line uses a post-Base dynamic reference.");
    }

    private static ulong AppendIntegerGroup(ulong value, byte next, int shift)
    {
        var part = (ulong)(next & 0x7f);
        if (part > (MaxInteger - value) >> shift)
            throw new Http3QpackException("A QPACK integer exceeds 62 bits.");
        return value + (part << shift);
    }

    private static string DecodeString(ReadOnlySpan<byte> encoded, bool huffman, int maximumLength)
    {
        if (huffman)
            return Http3QpackHuffman.Decode(encoded, maximumLength);
        if (encoded.Length > maximumLength)
            throw new Http3HeaderLimitException("The decoded field section is too large.");

        // QPACK strings are octets. Latin-1 preserves every octet for the HTTP validator and lets the
        // size check count wire bytes, without silently replacing malformed UTF-8 with another value.
        return Encoding.Latin1.GetString(encoded);
    }

    private static void WriteNameReference(ArrayBufferWriter<byte> writer, int index, string value)
    {
        WriteInteger(writer, 0x50, 4, index);
        WriteString(writer, value);
    }

    private static void WriteLiteral(ArrayBufferWriter<byte> writer, string name, string value)
    {
        // Never-indexed literals preserve the reference's field representation.
        WriteInteger(writer, 0x30, 3, name.Length);
        WriteAscii(writer, name);
        WriteString(writer, value);
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    {
        WriteInteger(writer, 0, 7, value.Length);
        WriteAscii(writer, value);
    }

    private static void WriteAscii(ArrayBufferWriter<byte> writer, string value)
    {
        var count = Encoding.ASCII.GetBytes(value, writer.GetSpan(value.Length));
        writer.Advance(count);
    }

    private static void WriteInteger(ArrayBufferWriter<byte> writer, byte prefix, int prefixBits, int value)
    {
        var mask = (1 << prefixBits) - 1;
        if (value < mask) {
            WriteByte(writer, (byte)(prefix | value));
            return;
        }
        WriteByte(writer, (byte)(prefix | mask));
        value -= mask;
        while (value >= 128) {
            WriteByte(writer, (byte)(0x80 | (value & 0x7f)));
            value >>= 7;
        }
        WriteByte(writer, (byte)value);
    }

    private static void WriteByte(ArrayBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }
}
