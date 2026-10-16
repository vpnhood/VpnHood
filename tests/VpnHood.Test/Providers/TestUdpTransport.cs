using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Test.Providers;

// A UDP transport with nothing behind it: it records its disposal
public class TestUdpTransport : IUdpTransport
{
    public bool IsDisposed { get; private set; }
    public Action<Memory<byte>>? DataReceived { get; set; }
    public int OverheadLength => 0;
    public bool Connected => !IsDisposed;

    public Task SendAsync(ReadOnlyMemory<byte> buffer)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
