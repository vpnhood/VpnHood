using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal sealed class Http3QpackSectionInput(Http3StreamReader reader, long length)
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
