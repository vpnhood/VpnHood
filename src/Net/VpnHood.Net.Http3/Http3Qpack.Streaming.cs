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

    public static async Task<Http3FieldSection> ReadSection(Http3StreamReader reader, long length,
        Action<KeyValuePair<string, string>>? onField, Http3ServerOptions options, Http3HeaderBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // The frame's declared size is not an allocation request. Decode each field as its bytes arrive,
        // keeping only bounded strings and at most the reference's 200-field parsing allowance.
        var input = new Http3QpackSectionInput(reader, length);
        var fields = new List<KeyValuePair<string, string>>();
        using var decoder = new FieldDecoder(options.MaxFieldBytes);
        try {
            var first = await input.ReadByte(cancellationToken).Vhc();
            if (await ReadInteger(input, first, 8, cancellationToken).Vhc() != 0)
                throw new Http3QpackException("A field section references the disabled dynamic table.");
            first = await input.ReadByte(cancellationToken).Vhc();
            // The reference ignores the sign bit and rejects a nonzero Delta Base.
            if (await ReadInteger(input, first, 7, cancellationToken).Vhc() != 0)
                throw new Http3QpackException("The field section has a nonzero Delta Base.");

            while (input.Remaining > 0) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReadBufferedField(input, decoder, out var completed, out var field)) {
                    var value = await input.ReadByte(cancellationToken).Vhc();
                    completed = decoder.ReadByte(value, input.Remaining + 1, out field);
                }
                if (!completed)
                    continue;
                budget.Append(field);
                onField?.Invoke(field);
                fields.Add(field);
            }
            if (decoder.HasPendingField)
                throw new Http3QpackException("The field section ended in a compressed value.");
            return new Http3FieldSection(fields, true, budget.ExceedsRequestLimits);
        }
        catch (EndOfStreamException) when (fields.Count > 0) {
            // Kestrel has already entered header parsing once a complete field was delivered. An
            // early FIN at this point ends the stream without a response, even for a huge declaration.
            return new Http3FieldSection(fields, false, false);
        }
    }

    private static bool ReadBufferedField(Http3QpackSectionInput input, FieldDecoder decoder,
        out bool completed, out KeyValuePair<string, string> field)
    {
        var bytes = input.GetBufferedBytes();
        if (bytes.IsEmpty) {
            completed = false;
            field = default;
            return false;
        }
        var consumed = 0;
        try {
            completed = decoder.Read(bytes, input.Remaining, out consumed, out field);
            return true;
        }
        finally {
            if (consumed != 0)
                input.ConsumeBuffered(consumed);
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

    private static string DecodeFieldString(ReadOnlySpan<byte> encoded, bool huffman, int maximumFieldBytes)
    {
        try {
            return DecodeString(encoded, huffman, maximumFieldBytes);
        }
        catch (Http3HeaderLimitException) {
            // A single oversized Huffman string is a compression error, not an HTTP 431.
            throw new Http3QpackException("A decoded QPACK string exceeds the reference's string limit.");
        }
    }
}
