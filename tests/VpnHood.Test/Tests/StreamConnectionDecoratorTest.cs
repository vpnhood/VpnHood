using VpnHood.Core.Tunneling.Connections;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// The decorator's two closes, over a connection whose async close is held, as one that waits for its peer would be
[TestClass]
public class StreamConnectionDecoratorTest : TestBase
{
    [TestMethod]
    public async Task A_quick_close_during_a_held_graceful_close_closes_the_inner_connection()
    {
        var innerStream = new CloseRecordingStream { HoldAsyncClose = true };
        var connection = new StreamConnectionDecorator(new TestStreamConnection(innerStream));

        var gracefulClose = connection.DisposeAsync().AsTask();
        connection.Dispose();
        Assert.IsTrue(innerStream.SyncClosed, "The quick close should close the inner connection at once.");

        innerStream.ReleaseAsyncClose();
        await gracefulClose;
    }

    [TestMethod]
    public async Task A_failed_graceful_close_of_its_stream_still_closes_the_inner_connection_with_no_reuse()
    {
        var inner = new TestStreamConnection(new MemoryStream());
        var connection = new StreamConnectionDecorator(inner, new CloseRecordingStream { FailAsyncClose = true });

        // its own stream's close failed, and the inner connection was left open, to be reused
        await connection.DisposeAsync();
        Assert.IsTrue(inner.IsDisposed);
        Assert.IsTrue(inner.IsReusePrevented);
    }

    [TestMethod]
    public void A_failed_quick_close_of_its_stream_still_closes_the_inner_connection_with_no_reuse()
    {
        var inner = new TestStreamConnection(new MemoryStream());
        var connection = new StreamConnectionDecorator(inner, new CloseRecordingStream { FailSyncClose = true });

        connection.Dispose();
        Assert.IsTrue(inner.IsDisposed);
        Assert.IsTrue(inner.IsReusePrevented);
    }

    [TestMethod]
    public async Task A_graceful_close_closes_its_stream_once_before_the_inner_connections_release()
    {
        // after its async close and the inner connection's release, it closed the stream again: a failure there barred
        // the reuse of a connection handed on already
        var inner = new TestStreamConnection(new MemoryStream());
        var stream = new CloseRecordingStream { FailSyncClose = true };
        var connection = new StreamConnectionDecorator(inner, stream);

        await connection.DisposeAsync();
        Assert.IsTrue(stream.AsyncClosed);
        Assert.IsFalse(stream.SyncClosed);
        Assert.IsFalse(inner.IsReusePrevented);
        Assert.IsTrue(inner.IsDisposed);
    }

    [TestMethod]
    public async Task A_second_graceful_close_waits_for_the_first_ones_end()
    {
        var innerStream = new CloseRecordingStream { HoldAsyncClose = true };
        var connection = new StreamConnectionDecorator(new TestStreamConnection(innerStream));

        var firstClose = connection.DisposeAsync().AsTask();
        var secondClose = connection.DisposeAsync().AsTask();
        Assert.IsFalse(secondClose.IsCompleted, "The second close should wait for the first one's end.");

        innerStream.ReleaseAsyncClose();
        await Task.WhenAll(firstClose, secondClose);
    }
}
