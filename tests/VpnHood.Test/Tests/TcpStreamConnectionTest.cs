using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// The connection's two closes, over a stream whose async close is held, as one that waits for its peer would be
[TestClass]
public class TcpStreamConnectionTest : TestBase
{
    // the client, connected to the peer it returns
    private async Task<TcpClient> Connect(TcpClient client)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            var acceptTask = listener.AcceptTcpClientAsync(TestCt).AsTask();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, TestCt);
            return await acceptTask;
        }
        finally {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task A_quick_close_during_a_held_graceful_close_closes_the_socket()
    {
        using var client = new TcpClient();
        using var peer = await Connect(client);
        var stream = new CloseRecordingStream { HoldAsyncClose = true };
        var connection = new TcpStreamConnection(client, stream, connectionName: "test", isServer: false);

        var gracefulClose = connection.DisposeAsync().AsTask();
        connection.Dispose();

        // the peer reads the socket's end
        var read = await peer.GetStream().ReadAsync(new byte[1], TestCt).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.AreEqual(0, read);

        stream.ReleaseAsyncClose();
        await gracefulClose;
    }

    [TestMethod]
    public async Task A_second_graceful_close_waits_for_the_first_ones_end()
    {
        using var client = new TcpClient();
        using var peer = await Connect(client);
        var stream = new CloseRecordingStream { HoldAsyncClose = true };
        var connection = new TcpStreamConnection(client, stream, connectionName: "test", isServer: false);

        var firstClose = connection.DisposeAsync().AsTask();
        var secondClose = connection.DisposeAsync().AsTask();
        Assert.IsFalse(secondClose.IsCompleted, "The second close should wait for the first one's end.");

        stream.ReleaseAsyncClose();
        await Task.WhenAll(firstClose, secondClose);
        Assert.AreEqual(1, stream.AsyncCloseCount);
    }
}
