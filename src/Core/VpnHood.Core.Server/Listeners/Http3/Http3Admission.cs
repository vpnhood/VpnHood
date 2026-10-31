using System.Buffers;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal static class Http3Admission
{
    public static async Task<(Http3PrefixStream Stream, bool Claimed)> Read(Stream source,
        Http3StreamContext context, IHttp3StreamFilter filter, int prefixLimit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(prefixLimit);
        var prefix = ArrayPool<byte>.Shared.Rent(prefixLimit);
        try {
            var result = await ReadPrefix(source, context, filter, prefix, prefixLimit, cancellationToken).Vhc();
            return (new Http3PrefixStream(source, prefix, result.Count), result.Claimed);
        }
        catch {
            ArrayPool<byte>.Shared.Return(prefix, clearArray: true);
            throw;
        }
    }

    private static async Task<(int Count, bool Claimed)> ReadPrefix(Stream source, Http3StreamContext context,
        IHttp3StreamFilter filter, byte[] prefix, int prefixLimit, CancellationToken cancellationToken)
    {
        var count = 0;
        var readLimit = prefixLimit;
        while (count < readLimit) {
            var read = await source.ReadAsync(prefix.AsMemory(count, readLimit - count), cancellationToken).Vhc();
            if (read == 0)
                break;
            count += read;
            var decision = await filter.Filter(context, prefix.AsMemory(0, count), cancellationToken).Vhc();
            if (decision != Http3StreamDecision.NeedMore)
                return (count, decision == Http3StreamDecision.Claimed);
            readLimit = GetReadLimit(prefix.AsSpan(0, count), prefixLimit);
        }
        return (count, false);
    }

    private static int GetReadLimit(ReadOnlySpan<byte> prefix, int prefixLimit)
    {
        var typeLength = 1 << (prefix[0] >> 6);
        if (prefix.Length < typeLength)
            return Math.Min(prefixLimit, typeLength);
        var offset = 0;
        var type = Http3Protocol.ReadInteger(prefix, ref offset);
        // Known request frames and errors go straight to HTTP/3. An extension frame may be
        // inspected only through its payload, so a raw header wait cannot hide the next error.
        if (type is 0 or 1 or 3 or 4 or 5 or 7 or 13)
            return typeLength;
        if (prefix.Length == typeLength)
            return Math.Min(prefixLimit, typeLength + 1);
        var lengthLength = 1 << (prefix[typeLength] >> 6);
        if (prefix.Length < typeLength + lengthLength)
            return Math.Min(prefixLimit, typeLength + lengthLength);
        var length = Http3Protocol.ReadInteger(prefix, ref offset);
        return (int)Math.Min(prefixLimit, offset + length);
    }
}
