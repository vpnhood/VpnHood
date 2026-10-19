using System.Net;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class QuicStreamConnectionTest : TestBase
{
    [TestMethod]
    public async Task A_failed_stream_close_still_reports_the_connection_closed()
    {
        // its QUIC connection counts its streams down on the report
        var stream = new CloseRecordingStream { FailAsyncClose = true };
        var connection = new QuicStreamConnection(stream, IPEndPoint.Parse("127.0.0.1:1"),
            IPEndPoint.Parse("127.0.0.1:2"), connectionName: "test", isServer: false);
        var disposedCount = 0;
        connection.Disposed += (_, _) => disposedCount++;

        await connection.DisposeAsync();

        Assert.IsTrue(stream.AsyncClosed);
        Assert.AreEqual(1, disposedCount);
    }
}
