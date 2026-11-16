using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal sealed class Http3QpackSectionInput(Http3StreamReader reader, long length)
{
    public long Remaining { get; private set; } = length;

    public ReadOnlySpan<byte> GetBufferedBytes() =>
        reader.GetBufferedBytes((int)Math.Min(Remaining, int.MaxValue));

    public void ConsumeBuffered(int count)
    {
        if (count > Remaining || !reader.TryReadBuffered(count, out _))
            throw new InvalidOperationException("The decoded field bytes are no longer buffered.");
        Remaining -= count;
    }

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
}
