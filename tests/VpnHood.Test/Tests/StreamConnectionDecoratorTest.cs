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
