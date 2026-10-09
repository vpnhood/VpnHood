using System.Buffers;

namespace VpnHood.Test.Providers;

// A packet's memory that records its disposal, so a test can see whether a transport disposed the packet
public class TestMemoryOwner(byte[] buffer) : IMemoryOwner<byte>
{
    private int _disposeCount;
    public bool IsDisposed => Volatile.Read(ref _disposeCount) > 0;
    public Memory<byte> Memory => buffer;

    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCount);
    }
}
