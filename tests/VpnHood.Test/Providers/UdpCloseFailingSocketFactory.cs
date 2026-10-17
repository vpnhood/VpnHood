using System.Net.Sockets;

namespace VpnHood.Test.Providers;

// A test socket factory whose UDP clients' closes throw once armed, after releasing their socket, as a close that
// fails
public class UdpCloseFailingSocketFactory : TestSocketFactory
{
    public volatile bool FailCloses; // closes from now on throw
    public int CreatedUdpClientCount;
    public int FailedCloseCount;

    public override UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        Interlocked.Increment(ref CreatedUdpClientCount);
        return new CloseFailingUdpClient(addressFamily, this);
    }

    private sealed class CloseFailingUdpClient(AddressFamily addressFamily, UdpCloseFailingSocketFactory owner)
        : UdpClient(addressFamily)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing || !owner.FailCloses)
                return;

            Interlocked.Increment(ref owner.FailedCloseCount);
            throw new IOException("Test: the UDP socket could not be closed.");
        }
    }
}
