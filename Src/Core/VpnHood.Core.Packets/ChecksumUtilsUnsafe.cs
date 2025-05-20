using System.Buffers.Binary;

namespace VpnHood.Core.Packets;

public static class ChecksumUtilsUnsafe
{
    public static ushort OnesComplementSum(ReadOnlySpan<byte> data)
    {
        return OnesComplementSum(ReadOnlySpan<byte>.Empty, data);
    }

    public static unsafe ushort OnesComplementSum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        ulong sum = 0;

        fixed (byte* pPseudo = pseudoHeader)
        fixed (byte* pData = data) {
            sum = SumFast(pPseudo, pseudoHeader.Length, sum);
            sum = SumFast(pData, data.Length, sum);
        }

        // Fold 64 → 32
        var low = (uint)sum;
        var high = (uint)(sum >> 32);
        low += high;
        if (low < high)
            low++;

        // Fold 32 → 16
        var s1 = (ushort)low;
        var s2 = (ushort)(low >> 16);
        s1 += s2;
        if (s1 < s2)
            s1++;

        var checksum = (ushort)~s1;
        return checksum == 0 ? (ushort)0xFFFF : checksum;
    }

    private static unsafe ulong SumFast(byte* ptr, int len, ulong sum)
    {
        while (len >= 8) {
            var val = BinaryPrimitives.ReverseEndianness(*(ulong*)ptr);
            sum += val;
            if (sum < val) sum++;
            ptr += 8;
            len -= 8;
        }

        if (len >= 4) {
            var val = BinaryPrimitives.ReverseEndianness(*(uint*)ptr);
            sum += val;
            if (sum < val) sum++;
            ptr += 4;
            len -= 4;
        }

        if (len >= 2) {
            var val = BinaryPrimitives.ReverseEndianness(*(ushort*)ptr);
            sum += val;
            if (sum < val) sum++;
            ptr += 2;
            len -= 2;
        }

        if (len == 1) {
            // Pad single byte as high byte
            var last = (ushort)(*ptr << 8);
            sum += last;
            if (sum < last) sum++;
        }

        return sum;
    }
}