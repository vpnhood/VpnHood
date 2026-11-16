using System.Collections.Concurrent;
using System.Threading.Channels;
using VpnHood.Net.Packets;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.TcpStack.Test;

/// <summary>
/// An adapter whose host is the test: a packet the test sends arrives as one the host routed into the tunnel, and
/// each packet the adapter writes comes back to the test, as the host would receive it. It needs no administrator.
/// </summary>
internal sealed class TestPacketAdapter()
    : NullVpnAdapter(new VpnAdapterSettings { AdapterName = "TestPacketAdapter", Blocking = false })
{
    private readonly ConcurrentQueue<byte[]> _fromHost = new();
    private readonly SemaphoreSlim _fromHostArrived = new(0);
    private readonly Channel<byte[]> _toHost = Channel.CreateUnbounded<byte[]>();

    /// <summary>Sends a packet from the host into the tunnel; the caller keeps the packet, which is copied.</summary>
    public void SendFromHost(IpPacket ipPacket)
    {
        _fromHost.Enqueue(ipPacket.Buffer.ToArray());
        _fromHostArrived.Release();
    }

    /// <summary>Receives the next packet the adapter wrote to the host; the caller owns it.</summary>
    public async Task<IpPacket> ReceiveAtHost(CancellationToken cancellationToken)
    {
        var bytes = await _toHost.Reader.ReadAsync(cancellationToken);
        return PacketBuilder.Parse(bytes);
    }

    // A short wait, not one until the stop: the reader's loop looks at its run after each wait, so a stop ends it.
    protected override void WaitForTunRead()
    {
        _fromHostArrived.Wait(TimeSpan.FromMilliseconds(50));
    }

    protected override int ReadPacket(byte[] buffer)
    {
        if (!_fromHost.TryDequeue(out var bytes))
            return 0;

        bytes.CopyTo(buffer, 0);
        return bytes.Length;
    }

    // the adapter's transport disposes the packet once written, so the host gets a copy
    protected override bool WritePacket(IpPacket ipPacket)
    {
        _toHost.Writer.TryWrite(ipPacket.Buffer.ToArray());
        return true;
    }

    protected override void DisposeManaged()
    {
        base.DisposeManaged();
        _toHost.Writer.TryComplete();
        _fromHostArrived.Dispose();
    }
}
