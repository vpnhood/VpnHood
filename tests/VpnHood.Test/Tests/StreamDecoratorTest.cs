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

    // With its source left open, a disposed wrapper works no more: the source is its owner's again
    [TestMethod]
    public async Task A_disposed_decorator_reads_and_writes_no_more_though_its_source_is_left_open()
    {
        using var source = new MemoryStream(new byte[10]);
        var decorator = new StreamDecoratorAsync(source, leaveOpen: true);
        await decorator.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await decorator.ReadAsync(new byte[1], TestCt));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await decorator.WriteAsync(new byte[1], TestCt));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => decorator.FlushAsync(TestCt));
        Assert.ThrowsExactly<ObjectDisposedException>(() => decorator.Seek(0, SeekOrigin.Begin));

        // the source itself is as it was
        Assert.AreEqual(0, source.ReadByte());
    }

    [TestMethod]
    public async Task A_disposed_buffered_stream_gives_what_it_buffered_no_more()
    {
        using var source = new MemoryStream(new byte[10]);
        var buffered = new ReadBufferedStream(source, leaveOpen: true, initData: [1, 2, 3]);
        Assert.AreEqual(1, await buffered.ReadAsync(new byte[1], TestCt));
        await buffered.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await buffered.ReadAsync(new byte[1], TestCt));
    }

    [TestMethod]
    public void A_disposed_window_reads_and_seeks_no_more_though_its_source_is_left_open()
    {
        using var source = new MemoryStream(new byte[10]);
        var window = new ReadOnlySubStream(source, start: 2, length: 5, leaveOpen: true);
        Assert.AreEqual(0, window.ReadByte());
        window.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => window.ReadByte());
        Assert.ThrowsExactly<ObjectDisposedException>(() => window.Seek(0, SeekOrigin.Begin));
        Assert.AreEqual(0, source.ReadByte());
    }

    // a start and a length that overflow together add up to an end before the source's
    [TestMethod]
    public void A_window_that_runs_past_its_source_is_refused_whatever_its_numbers_add_up_to()
    {
        using var source = new MemoryStream(new byte[10]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReadOnlySubStream(source, long.MaxValue, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReadOnlySubStream(source, 5, long.MaxValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReadOnlySubStream(source, 5, 6));
        using var whole = new ReadOnlySubStream(source, 5, 5, leaveOpen: true);
        Assert.AreEqual(5, whole.Length);
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
