using System.Buffers;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal static partial class Http3Qpack
{
    public static Task<Http3FieldSection> ReadSection(Http3StreamReader reader, long length,
        Http3ServerOptions options, CancellationToken cancellationToken) =>
        ReadSection(reader, length, null, options, cancellationToken);

    public static Task<Http3FieldSection> ReadSection(Http3StreamReader reader, long length,
        Action<KeyValuePair<string, string>>? onField, Http3ServerOptions options, CancellationToken cancellationToken) =>
        ReadSection(reader, length, onField, options, new Http3HeaderBudget(options), cancellationToken);

    public static Task<Http3FieldSection> ReadSection(Http3StreamReader reader, long length,
        Action<KeyValuePair<string, string>>? onField, Http3ServerOptions options, Http3HeaderBudget budget,
        CancellationToken cancellationToken) =>
        ReadSection(reader, length, onField, options, budget, null, cancellationToken);

    public static async Task<Http3FieldSection> ReadSection(Http3StreamReader reader, long length,
        Action<KeyValuePair<string, string>>? onField, Http3ServerOptions options, Http3HeaderBudget budget,
        Http3TrailerInspection? inspection, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // The frame's declared size is not an allocation request. Decode each field as its bytes arrive,
        // keeping only bounded strings and at most the reference's 200-field parsing allowance.
        var input = new Http3QpackSectionInput(reader, length, inspection);
        var fields = new List<KeyValuePair<string, string>>();
        try {
            var first = await input.ReadByte(cancellationToken).Vhc();
            if (await ReadInteger(input, first, 8, cancellationToken).Vhc() != 0)
                throw new Http3QpackException("A field section references the disabled dynamic table.");
            first = await input.ReadByte(cancellationToken).Vhc();
            // The reference ignores the sign bit and rejects a nonzero Delta Base.
            if (await ReadInteger(input, first, 7, cancellationToken).Vhc() != 0)
                throw new Http3QpackException("The field section has a nonzero Delta Base.");

            input.StartFields();
            while (input.Remaining > 0) {
                var field = await ReadField(input, options.MaxFieldBytes, cancellationToken).Vhc();
                budget.Append(field);
                onField?.Invoke(field);
                fields.Add(field);
                inspection?.FieldCompleted();
            }
            return new Http3FieldSection(fields, true, budget.ExceedsRequestLimits);
        }
        catch (EndOfStreamException) when (fields.Count > 0) {
            // Kestrel has already entered header parsing once a complete field was delivered. An
            // early FIN at this point ends the stream without a response, even for a huge declaration.
            return new Http3FieldSection(fields, false, false);
        }
    }

    private static async ValueTask<KeyValuePair<string, string>> ReadField(Http3QpackSectionInput input,
        int maximumFieldBytes, CancellationToken cancellationToken)
    {
        var first = await input.ReadByte(cancellationToken).Vhc();
        switch (GetFieldKind(first)) {
            case Http3QpackFieldKind.Indexed:
                return Http3QpackStaticTable.Get(await ReadInteger(input, first, 6, cancellationToken).Vhc());
            case Http3QpackFieldKind.NameReference:
                var index = await ReadInteger(input, first, 4, cancellationToken).Vhc();
                var name = Http3QpackStaticTable.Get(index).Key;
                first = await input.ReadByte(cancellationToken).Vhc();
                var value = await ReadString(input, first, 7, 0x80, maximumFieldBytes, cancellationToken).Vhc();
                return new KeyValuePair<string, string>(name, value);
            default:
                name = await ReadString(input, first, 3, 0x08, maximumFieldBytes, cancellationToken).Vhc();
                if (name.Length == 0)
                    throw new Http3QpackException("A literal field name is empty.");
                first = await input.ReadByte(cancellationToken).Vhc();
                value = await ReadString(input, first, 7, 0x80, maximumFieldBytes, cancellationToken).Vhc();
                return new KeyValuePair<string, string>(name, value);
        }
    }

    private static async ValueTask<ulong> ReadInteger(Http3QpackSectionInput input, byte first, int prefixBits,
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

    private static async ValueTask<string> ReadString(Http3QpackSectionInput input, byte first, int prefixBits,
        int huffmanMask, int maximumFieldBytes, CancellationToken cancellationToken)
    {
        var huffman = (first & huffmanMask) != 0;
        var length = await ReadInteger(input, first, prefixBits, cancellationToken).Vhc();
        if (length > (ulong)maximumFieldBytes)
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
                return DecodeString(encoded.AsSpan(0, count), huffman, maximumFieldBytes);
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
}
