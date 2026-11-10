using System.Buffers;

namespace VpnHood.Test.Providers;

// A memory pool that lends TestMemoryOwners and keeps them, so a test can see whether what it lent was disposed
public class TestMemoryPool : MemoryPool<byte>
{
    private readonly List<TestMemoryOwner> _rented = [];

    public IReadOnlyList<TestMemoryOwner> Rented => _rented;

    public override int MaxBufferSize => int.MaxValue;

    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        var memoryOwner = new TestMemoryOwner(new byte[minBufferSize == -1 ? 4096 : minBufferSize]);
        _rented.Add(memoryOwner);
        return memoryOwner;
    }

    protected override void Dispose(bool disposing)
    {
    }
}
