using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal sealed class Http3QpackSectionInput(Http3StreamReader reader, long length, Http3TrailerInspection? inspection)
{
    private bool _fieldsStarted;
    public long Remaining { get; private set; } = length;

    // The QPACK prefix is framing; the sampled distance starts with the encoded fields.
    public void StartFields() => _fieldsStarted = true;

    public async ValueTask<byte> ReadByte(CancellationToken cancellationToken)
    {
        if (Remaining == 0)
            throw new Http3QpackException("The field section ended in a compressed value.");
        if (_fieldsStarted)
            inspection?.CheckForEmission();
        var value = await reader.ReadByte(cancellationToken).Vhc();
        if (value < 0)
            throw new EndOfStreamException();
        Remaining--;
        if (_fieldsStarted)
            inspection?.Advance(1);
        return (byte)value;
    }

    public async ValueTask<int> Read(Memory<byte> bytes, CancellationToken cancellationToken)
    {
        var requested = (int)Math.Min(bytes.Length, Remaining);
        var allowed = _fieldsStarted && inspection != null ? inspection.LimitRead(requested) : requested;
        var count = await reader.Read(bytes[..allowed], cancellationToken).Vhc();
        if (count == 0)
            throw new EndOfStreamException();
        Remaining -= count;
        if (_fieldsStarted)
            inspection?.Advance(count);
        return count;
    }
}
