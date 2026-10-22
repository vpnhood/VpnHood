using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace VpnHood.Net.Packets;

public static class ChecksumUtils
{
    public static ushort OnesComplementSum(ReadOnlySpan<byte> data)
    {
        return OnesComplementSum(ReadOnlySpan<byte>.Empty, data);
    }

    // The Internet checksum (RFC 1071): the complement of the ones' complement sum of the 16-bit words. The words
    // are added as native 32-bit ones into 64-bit sums, which no carry leaves, then folded and turned to network
    // order once (RFC 1071 2(B)): a carry checked per word is a branch that random data mispredicts half the time.
    // A computed zero is returned as is; UDP sends it as all ones itself.
    public static ushort OnesComplementSum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        var folded = Fold(Sum(pseudoHeader) + Sum(data));
        if (BitConverter.IsLittleEndian)
            folded = BinaryPrimitives.ReverseEndianness(folded);

        return (ushort)~folded;
    }

    // Vector256 runs on x64 and AdvSimd on Arm64. Mono, the Android app's runtime, has neither without LLVM and
    // compiles Vector128's loads and shifts as calls, so it takes the scalar loop.
    private static ulong Sum(ReadOnlySpan<byte> data)
    {
        ref var start = ref MemoryMarshal.GetReference(data);
        var length = data.Length;
        var offset = 0;
        ulong sum = 0;

        if (Vector256.IsHardwareAccelerated && length >= 64) {
            offset = length & ~63;
            sum = SumVector256(ref start, offset);
        }
        else if (AdvSimd.IsSupported && length >= 64) {
            offset = length & ~63;
            sum = SumAdvSimd(ref start, offset);
        }
        else if (length >= 32) {
            offset = length & ~31;
            sum = SumScalar(ref start, offset);
        }

        for (; offset <= length - 8; offset += 8) {
            var words = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, offset));
            sum += (uint)words + (words >> 32);
        }

        if (offset <= length - 4) {
            sum += Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref start, offset));
            offset += 4;
        }

        if (offset <= length - 2) {
            sum += Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref start, offset));
            offset += 2;
        }

        // an odd last byte is the high byte of its network-order word
        if (offset < length) {
            var last = Unsafe.Add(ref start, offset);
            sum += BitConverter.IsLittleEndian ? last : (uint)last << 8;
        }

        return sum;
    }

    // each 64-bit lane takes the low and the high 32-bit word of its data apart; four sums let the adds overlap
    private static ulong SumVector256(ref byte start, int length)
    {
        var lowWords = Vector256.Create(0xFFFF_FFFFUL);
        var sum0 = Vector256<ulong>.Zero;
        var sum1 = Vector256<ulong>.Zero;
        var sum2 = Vector256<ulong>.Zero;
        var sum3 = Vector256<ulong>.Zero;
        for (nuint offset = 0; offset < (nuint)length; offset += 64) {
            var data0 = Vector256.LoadUnsafe(ref start, offset).AsUInt64();
            var data1 = Vector256.LoadUnsafe(ref start, offset + 32).AsUInt64();
            sum0 += data0 & lowWords;
            sum1 += data0 >>> 32;
            sum2 += data1 & lowWords;
            sum3 += data1 >>> 32;
        }

        return Vector256.Sum(sum0 + sum1 + (sum2 + sum3));
    }

    // UADALP adds each pair of 32-bit words into its 64-bit lane: one instruction per 16 bytes
    private static ulong SumAdvSimd(ref byte start, int length)
    {
        var sum0 = Vector128<ulong>.Zero;
        var sum1 = Vector128<ulong>.Zero;
        var sum2 = Vector128<ulong>.Zero;
        var sum3 = Vector128<ulong>.Zero;
        for (nuint offset = 0; offset < (nuint)length; offset += 64) {
            sum0 = AdvSimd.AddPairwiseWideningAndAdd(sum0, Vector128.LoadUnsafe(ref start, offset).AsUInt32());
            sum1 = AdvSimd.AddPairwiseWideningAndAdd(sum1, Vector128.LoadUnsafe(ref start, offset + 16).AsUInt32());
            sum2 = AdvSimd.AddPairwiseWideningAndAdd(sum2, Vector128.LoadUnsafe(ref start, offset + 32).AsUInt32());
            sum3 = AdvSimd.AddPairwiseWideningAndAdd(sum3, Vector128.LoadUnsafe(ref start, offset + 48).AsUInt32());
        }

        return Vector128.Sum(sum0 + sum1 + (sum2 + sum3));
    }

    // four sums let the adds overlap: unrolled, it takes half the time of one 8-byte add a step
    private static ulong SumScalar(ref byte start, int length)
    {
        ulong sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
        for (nuint offset = 0; offset < (nuint)length; offset += 32) {
            var words0 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, offset));
            var words1 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, offset + 8));
            var words2 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, offset + 16));
            var words3 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, offset + 24));
            sum0 += (uint)words0 + (words0 >> 32);
            sum1 += (uint)words1 + (words1 >> 32);
            sum2 += (uint)words2 + (words2 >> 32);
            sum3 += (uint)words3 + (words3 >> 32);
        }

        return sum0 + sum1 + (sum2 + sum3);
    }

    // the 64-bit sum folded to 16 bits, each fold's carry added back in
    private static ushort Fold(ulong sum)
    {
        sum = (sum >> 32) + (uint)sum;
        sum = (sum >> 32) + (uint)sum;
        sum = (sum >> 16) + (ushort)sum;
        sum = (sum >> 16) + (ushort)sum;
        return (ushort)sum;
    }
}
