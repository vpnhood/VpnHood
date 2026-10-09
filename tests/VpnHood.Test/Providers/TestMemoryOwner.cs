using System.Buffers;

namespace VpnHood.Test.Providers;

// A packet's memory that records its disposal, so a test can see whether a transport disposed the packet
public class TestMemoryOwner(byte[] buffer) : IMemoryOwner<byte>
{
    public bool IsDisposed { get; private set; }
    public Memory<byte> Memory => buffer;

    public void Dispose()
    {
        IsDisposed = true;
    }
}
