using VpnHood.Net.Toolkit.Streams;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class StreamDecoratorTest : TestBase
{
    // a decorator that records which cleanup its dispose ran
    private sealed class CleanupRecordingDecorator(Stream sourceStream, bool leaveOpen)
        : StreamDecoratorAsync(sourceStream, leaveOpen)
    {
        public bool? Disposing { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposing ??= disposing;
            base.Dispose(disposing);
        }
    }

    [TestMethod]
    public void A_sync_dispose_runs_the_cleanup_and_closes_the_source()
    {
        var source = new CloseRecordingStream();
        var stream = new CleanupRecordingDecorator(source, leaveOpen: false);

        stream.Dispose();

        Assert.AreEqual(true, stream.Disposing);
        Assert.IsTrue(source.SyncClosed);
    }

    // the sync close's cleanup would close the source again, synchronously, which waits for a QUIC stream's peer
    [TestMethod]
    public async Task An_async_dispose_closes_the_source_only_asynchronously()
    {
        var source = new CloseRecordingStream();
        var stream = new CleanupRecordingDecorator(source, leaveOpen: false);

        await stream.DisposeAsync();

        Assert.AreEqual(false, stream.Disposing);
        Assert.IsTrue(source.AsyncClosed);
        Assert.IsFalse(source.SyncClosed);
    }

    [TestMethod]
    public async Task An_async_dispose_after_a_sync_one_does_not_close_the_source_again()
    {
        var source = new CloseRecordingStream();
        var stream = new CleanupRecordingDecorator(source, leaveOpen: false);

        // ReSharper disable once MethodHasAsyncOverload
        stream.Dispose();
        await stream.DisposeAsync();

        Assert.IsTrue(source.SyncClosed);
        Assert.IsFalse(source.AsyncClosed);
    }

    [TestMethod]
    public async Task Two_async_disposes_at_once_close_the_source_once()
    {
        var source = new CloseRecordingStream { HoldAsyncClose = true };
        var stream = new CleanupRecordingDecorator(source, leaveOpen: false);

        var first = stream.DisposeAsync().AsTask();
        var second = stream.DisposeAsync().AsTask(); // while the first one's source close is held
        source.ReleaseAsyncClose();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), TestCt);

        Assert.AreEqual(1, source.AsyncCloseCount);
    }
}
