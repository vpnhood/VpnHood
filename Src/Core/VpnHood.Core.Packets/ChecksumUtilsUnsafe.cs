using System;
using System.Buffers.Binary;

namespace VpnHood.Core.Packets;

public static class ChecksumUtils
{
    public static ushort OnesComplementSum(ReadOnlySpan<byte> data)
    {
        return OnesComplementSum(ReadOnlySpan<byte>.Empty, data);
    }

    public static ushort OnesComplementSum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        ulong sum = 0;

        // Proper padding of odd-length inputs for both pseudoHeader and data
        if (pseudoHeader.Length % 2 != 0)
            pseudoHeader = PadIfOdd(pseudoHeader);

        if (data.Length % 2 != 0)
            data = PadIfOdd(data);

        sum = SumSpan(pseudoHeader, sum);
        sum = SumSpan(data, sum);

        // Fold 64-bit → 32-bit
        uint low = (uint)sum;
        uint high = (uint)(sum >> 32);
        low += high;
        if (low < high) low++;

        // Fold 32-bit → 16-bit
        ushort s1 = (ushort)low;
        ushort s2 = (ushort)(low >> 16);
        s1 += s2;
        if (s1 < s2) s1++;

        ushort checksum = (ushort)~s1;
        return checksum == 0 ? (ushort)0xFFFF : checksum;
    }

    private static ulong SumSpan(ReadOnlySpan<byte> span, ulong sum)
    {
        int i = 0;
        while (i + 1 < span.Length)
        {
            ushort word = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(i, 2));
            sum += word;
            if (sum < word) sum++;
            i += 2;
        }

        // Handle final odd byte (shouldn't happen due to padding, but safety check)
        if (i < span.Length)
        {
            ushort last = (ushort)(span[i] << 8); // pad as high byte
            sum += last;
            if (sum < last) sum++;
        }

        return sum;
    }

    private static ReadOnlySpan<byte> PadIfOdd(ReadOnlySpan<byte> data)
    {
        // This function is only called if padding is needed
        byte[] padded = new byte[data.Length + 1];
        data.CopyTo(padded);
        padded[^1] = 0x00; // pad with zero
        return padded;
    }
}


