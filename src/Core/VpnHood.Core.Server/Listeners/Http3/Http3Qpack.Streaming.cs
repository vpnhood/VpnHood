using System.Buffers;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.Listeners.Http3;

internal static partial class Http3Qpack
{
    public static Task<Http3FieldSection> ReadSection(Http3StreamReader reader, long length,
        CancellationToken cancellationToken) => ReadSection(reader, length, null, cancellationToken);

    public static async Task<Http3FieldSection> ReadSection(Http3StreamReader reader, long length,
        Action<KeyValuePair<string, string>>? onField, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // The frame's declared size is not an allocation request. Decode each field as its bytes arrive,
        // keeping only bounded strings and at most the reference's 200-field parsing allowance.
        var input = new SectionInput(reader, length);
        var fields = new List<KeyValuePair<string, string>>();
        var decodedSize = 0;
        try {
            var first = await input.ReadByte(cancellationToken).Vhc();
            if (await ReadInteger(input, first, 8, cancellationToken).Vhc() != 0)
                throw new Http3QpackException("A field section references the disabled dynamic table.");
            first = await input.ReadByte(cancellationToken).Vhc();
            var negativeBase = (first & 0x80) != 0;
            _ = await ReadInteger(input, first, 7, cancellationToken).Vhc();
            if (negativeBase)
                throw new Http3QpackException("The field section has a negative Base.");

            while (input.Remaining > 0) {
                var field = await ReadField(input, cancellationToken).Vhc();
                decodedSize += field.Key.Length + field.Value.Length;
                if (decodedSize > MaxHeaderBytes * 2)
                    throw new Http3ProtocolException(Http3Protocol.RequestRejected, connectionError: false);
                if (fields.Count == MaxHeaderCount * 2)
                    throw new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);
                onField?.Invoke(field);
                fields.Add(field);
            }
            return new Http3FieldSection(fields, true,
                fields.Count > MaxHeaderCount || decodedSize > MaxHeaderBytes);
        }
        catch (EndOfStreamException) when (fields.Count > 0) {
            // Kestrel has already entered header parsing once a complete field was delivered. An
            // early FIN at this point ends the stream without a response, even for a huge declaration.
            return new Http3FieldSection(fields, false, false);
        }
    }

    private static async ValueTask<KeyValuePair<string, string>> ReadField(SectionInput input,
        CancellationToken cancellationToken)
    {
        var first = await input.ReadByte(cancellationToken).Vhc();
        switch (GetFieldKind(first)) {
            case FieldKind.Indexed:
                return Http3QpackStaticTable.Get(await ReadInteger(input, first, 6, cancellationToken).Vhc());
            case FieldKind.NameReference:
                var index = await ReadInteger(input, first, 4, cancellationToken).Vhc();
                var name = Http3QpackStaticTable.Get(index).Key;
                first = await input.ReadByte(cancellationToken).Vhc();
                var value = await ReadString(input, first, 7, 0x80, cancellationToken).Vhc();
                return new KeyValuePair<string, string>(name, value);
            default:
                name = await ReadString(input, first, 3, 0x08, cancellationToken).Vhc();
                first = await input.ReadByte(cancellationToken).Vhc();
                value = await ReadString(input, first, 7, 0x80, cancellationToken).Vhc();
                return new KeyValuePair<string, string>(name, value);
        }
    }

    private static async ValueTask<ulong> ReadInteger(SectionInput input, byte first, int prefixBits,
        CancellationToken cancellationToken)
    {
        var mask = (1 << prefixBits) - 1;
        var value = (ulong)(first & mask);
        if (value < (ulong)mask)
            return value;
        for (var shift = 0; shift <= 56; shift += 7) {
            var next = await input.ReadByte(cancellationToken).Vhc();
            value = AppendIntegerGroup(value, next, shift);
            if ((next & 0x80) == 0)
                return value;
        }
        throw new Http3QpackException("A QPACK integer has too many continuation bytes.");
    }

    private static async ValueTask<string> ReadString(SectionInput input, byte first, int prefixBits,
        int huffmanMask, CancellationToken cancellationToken)
    {
        var huffman = (first & huffmanMask) != 0;
        var length = await ReadInteger(input, first, prefixBits, cancellationToken).Vhc();
        if (length > MaxHeaderBytes)
            throw new Http3QpackException("A QPACK string exceeds the reference's individual string limit.");
        if (length > (ulong)input.Remaining)
            throw new Http3QpackException("A compressed string is truncated.");
        if (length == 0)
            return "";

        // Do not reserve a declared 32 KB value while its sender provides no value bytes. Start only
        // after the first byte arrives, then grow geometrically as the current small buffer fills.
        var firstValue = await input.ReadByte(cancellationToken).Vhc();
        var encoded = ArrayPool<byte>.Shared.Rent(Math.Min((int)length, 256));
        var count = 1;
        encoded[0] = firstValue;
        try {
            while ((ulong)count < length) {
                if (count == encoded.Length) {
                    var larger = ArrayPool<byte>.Shared.Rent(Math.Min((int)length, encoded.Length * 2));
                    encoded.AsSpan(0, count).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(encoded);
                    encoded = larger;
                }
                var wanted = Math.Min((int)length - count, encoded.Length - count);
                count += await input.Read(encoded.AsMemory(count, wanted), cancellationToken).Vhc();
            }
            try {
                return DecodeString(encoded.AsSpan(0, count), huffman, MaxHeaderBytes);
            }
            catch (Http3HeaderLimitException) {
                // An individual Huffman string over the reference's decoder limit is a compression
                // error. A completed section's total size is checked separately for an HTTP 431.
                throw new Http3QpackException("A decoded QPACK string exceeds the reference's string limit.");
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(encoded);
        }
    }

    private sealed class SectionInput(Http3StreamReader reader, long length)
    {
        public long Remaining { get; private set; } = length;

        public async ValueTask<byte> ReadByte(CancellationToken cancellationToken)
        {
            if (Remaining == 0)
                throw new Http3QpackException("The field section ended in a compressed value.");
            var value = await reader.ReadByte(cancellationToken).Vhc();
            if (value < 0)
                throw new EndOfStreamException();
            Remaining--;
            return (byte)value;
        }

        public async ValueTask<int> Read(Memory<byte> bytes, CancellationToken cancellationToken)
        {
            var count = await reader.Read(bytes[..(int)Math.Min(bytes.Length, Remaining)],
                cancellationToken).Vhc();
            if (count == 0)
                throw new EndOfStreamException();
            Remaining -= count;
            return count;
        }
    }
}
