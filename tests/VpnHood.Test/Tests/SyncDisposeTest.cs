using System.Net;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// A sync close waits for no peer: a service's stop runs one, and the OS kills a process whose stop hangs
[TestClass]
public class SyncDisposeTest : TestBase
{
    [TestMethod]
    public async Task A_quic_stream_connection_does_not_wait_for_its_stream_close()
    {
        // held, as a .NET QUIC stream's close waits for its peer
        var stream = new CloseRecordingStream { HoldAsyncClose = true };
        var connection = new QuicStreamConnection(stream, IPEndPoint.Parse("127.0.0.1:1"),
            IPEndPoint.Parse("127.0.0.1:2"), connectionName: "test", isServer: false);

        try {
            await Task.Run(connection.Dispose, TestCt).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            Assert.IsFalse(connection.Connected);
            Assert.IsTrue(stream.AsyncClosed);
            Assert.IsFalse(stream.SyncClosed);
        }
        finally {
            stream.ReleaseAsyncClose();
        }
    }
}
