namespace VpnHood.Core.Packets;

public static class ChecksumUtils
{
    public static ushort OnesComplementSum(ReadOnlySpan<byte> data)
    {
        return OnesComplementSum(ReadOnlySpan<byte>.Empty, data);
    }

    public static ushort OnesComplementSum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        sum += ComputeSumWords(pseudoHeader);
        sum += ComputeSumWords(data);

        // Fold into 16 bits until it fits
        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);


        // Invert and fix 0
        var checksum = (ushort)(~sum & 0xFFFF);
        if (checksum == 0)
            checksum = 0xFFFF;

        return checksum;
    }

    public static uint ComputeSumWords(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var i = 0; i < data.Length; i += 2) {
            var word = (ushort)(data[i] << 8 | (i + 1 < data.Length ? data[i + 1] : 0));
            sum += word;
        }
        return sum;
    }
}