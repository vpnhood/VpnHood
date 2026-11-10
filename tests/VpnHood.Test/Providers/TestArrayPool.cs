using System.Buffers;

namespace VpnHood.Test.Providers;

// An array pool that lends new arrays and records what it gets back, so a test can see a packet return its array once
public class TestArrayPool : ArrayPool<byte>
{
    private readonly List<byte[]> _returned = [];

    // set, its first return throws, as no pool should: a fault where none is expected
    public bool FailsFirstReturn { get; set; }

    public IReadOnlyList<byte[]> Returned => _returned;

    public override byte[] Rent(int minimumLength) => new byte[minimumLength];

    public override void Return(byte[] array, bool clearArray = false)
    {
        _returned.Add(array);
        if (_returned.Count == 1 && FailsFirstReturn)
            throw new InvalidOperationException("Test: the return failed.");
    }
}
