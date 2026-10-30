using System.Buffers;
using System.Globalization;
using System.Text;

namespace VpnHood.Core.Server.Listeners.Http3;

// RFC 9204 field sections with no dynamic table. The connection advertises zero table capacity and
// zero blocked streams, so a dynamic reference is a decoding error, never a reason to retain a block.
// This class decodes compression only; the HTTP/3 request parser validates header semantics.
internal static partial class Http3Qpack
{
    public const int MaxHeaderBytes = 32768;
    public const int MaxHeaderCount = 100;
    private const int FieldOverhead = 32;
    private const ulong MaxInteger = (1UL << 62) - 1;

    public static IReadOnlyList<KeyValuePair<string, string>> Decode(ReadOnlySpan<byte> block,
        int maxHeaderBytes = MaxHeaderBytes, int maxHeaderCount = MaxHeaderCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxHeaderBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxHeaderCount);

        var offset = 0;
        if (ReadInteger(block, ref offset, 8) != 0)
            throw new Http3QpackException("A field section references the disabled dynamic table.");

        var negativeBase = (Peek(block, offset) & 0x80) != 0;
        _ = ReadInteger(block, ref offset, 7);
        // With Required Insert Count zero, a negative delta always produces a forbidden negative Base.
        // A positive Base is allowed even without dynamic references (RFC 9204 section 4.5.1.2).
        if (negativeBase)
            throw new Http3QpackException("The field section has a negative Base.");

        var headers = new List<KeyValuePair<string, string>>();
        var decodedSize = 0;
        while (offset < block.Length) {
            if (headers.Count == maxHeaderCount)
                throw new Http3HeaderLimitException("The field section contains too many headers.");
            var remaining = maxHeaderBytes - decodedSize - FieldOverhead;
            if (remaining < 0)
                throw new Http3HeaderLimitException("The decoded field section is too large.");

            var first = block[offset];
            var kind = GetFieldKind(first);
            string name;
            string value;
            if (kind == FieldKind.Indexed) {
                // Indexed field line: 1 T Index(6+).
                var field = Http3QpackStaticTable.Get(ReadInteger(block, ref offset, 6));
                name = field.Key;
                value = field.Value;
            }
            else if (kind == FieldKind.NameReference) {
                // Literal value with name reference: 01 N T Index(4+). N does not change decoding.
                name = Http3QpackStaticTable.Get(ReadInteger(block, ref offset, 4)).Key;
                value = ReadString(block, ref offset, 7, 0x80, remaining - name.Length);
            }
            else {
                // Literal name and value: 001 N H NameLength(3+), name, H ValueLength(7+), value.
                name = ReadString(block, ref offset, 3, 0x08, remaining);
                value = ReadString(block, ref offset, 7, 0x80, remaining - name.Length);
            }

            // Count decoded octets plus the per-field allowance required by HTTP/3's field-size setting.
            // A short compressed block can expand substantially through repeated indexed fields.
            if (name.Length > remaining || value.Length > remaining - name.Length)
                throw new Http3HeaderLimitException("The decoded field section is too large.");
            decodedSize += name.Length + value.Length + FieldOverhead;
            headers.Add(new KeyValuePair<string, string>(name, value));
        }
        return headers;
    }

    public static byte[] EncodeResponse(int statusCode, DateTimeOffset date)
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
        WriteNameReference(writer, 6, date.UtcDateTime.ToString("R", CultureInfo.InvariantCulture));
        WriteNameReference(writer, 92, "Kestrel");
        if (statusCode == 401)
            WriteLiteral(writer, "www-authenticate", "Bearer");
        WriteInteger(writer, 0xc0, 6, 4); // content-length: 0.
        return writer.WrittenSpan.ToArray();
    }

    private static byte Peek(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset >= bytes.Length)
            throw new Http3QpackException("The field section ended in a compressed value.");
        return bytes[offset];
    }

    private enum FieldKind { Indexed, NameReference, Literal }

    private static FieldKind GetFieldKind(byte first)
    {
        if ((first & 0x80) != 0) {
            if ((first & 0x40) == 0)
                throw new Http3QpackException("A field line references the disabled dynamic table.");
            return FieldKind.Indexed;
        }
        if ((first & 0x40) != 0) {
            if ((first & 0x10) == 0)
                throw new Http3QpackException("A field name references the disabled dynamic table.");
            return FieldKind.NameReference;
        }
        if ((first & 0x20) != 0)
            return FieldKind.Literal;
        throw new Http3QpackException("A field line uses a post-Base dynamic reference.");
    }

    private static ulong ReadInteger(ReadOnlySpan<byte> bytes, ref int offset, int prefixBits)
    {
        var prefixMask = (1 << prefixBits) - 1;
        var value = (ulong)(Peek(bytes, offset++) & prefixMask);
        if (value < (ulong)prefixMask)
            return value;

        // QPACK requires support through 62 bits. Check before shifting/adding, including the last
        // group, so malformed lengths and indexes cannot wrap into small valid values.
        for (var shift = 0; shift <= 56; shift += 7) {
            var next = Peek(bytes, offset++);
            value = AppendIntegerGroup(value, next, shift);
            if ((next & 0x80) == 0)
                return value;
        }
        throw new Http3QpackException("A QPACK integer has too many continuation bytes.");
    }

    private static ulong AppendIntegerGroup(ulong value, byte next, int shift)
    {
        var part = (ulong)(next & 0x7f);
        if (part > (MaxInteger - value) >> shift)
            throw new Http3QpackException("A QPACK integer exceeds 62 bits.");
        return value + (part << shift);
    }

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int offset, int prefixBits,
        int huffmanMask, int maximumLength)
    {
        if (maximumLength < 0)
            throw new Http3HeaderLimitException("The decoded field section is too large.");
        var huffman = (Peek(bytes, offset) & huffmanMask) != 0;
        var length = ReadInteger(bytes, ref offset, prefixBits);
        if (length > (ulong)(bytes.Length - offset))
            throw new Http3QpackException("A compressed string is truncated.");
        var encoded = bytes.Slice(offset, (int)length);
        offset += (int)length;
        return DecodeString(encoded, huffman, maximumLength);
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
        // Match the reference's never-indexed literal representation for the authentication challenge.
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
