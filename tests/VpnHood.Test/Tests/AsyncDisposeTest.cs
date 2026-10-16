using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// An async close runs no sync one: a sync close may block, as a QUIC stream's waits for its peer
[TestClass]
public class AsyncDisposeTest : TestBase
{
    // one whose async close fails, and that records whether its sync one ran
    private sealed class FailingAsyncDisposable : IAsyncDisposable, IDisposable
    {
        public bool SyncDisposed { get; private set; }

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new IOException("An async close failure for the test."));

        public void Dispose() => SyncDisposed = true;
    }

    // a sub-stream that records which cleanup its dispose ran
    private sealed class CleanupRecordingSubStream(Stream source) : ReadOnlySubStream(source, 0, 0)
    {
        public bool? Disposing { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposing ??= disposing;
            base.Dispose(disposing);
        }
    }

    [TestMethod]
    public async Task TryDisposeAsync_runs_the_async_close_only()
    {
        var stream = new CloseRecordingStream();

        await stream.TryDisposeAsync();

        Assert.IsTrue(stream.AsyncClosed);
        Assert.IsFalse(stream.SyncClosed);
    }

    [TestMethod]
    public async Task TryDisposeAsync_runs_no_sync_close_after_a_failed_async_one()
    {
        var disposable = new FailingAsyncDisposable();

        await disposable.TryDisposeAsync();

        Assert.IsFalse(disposable.SyncDisposed);
    }

    [TestMethod]
    public async Task A_sub_stream_closes_its_source_only_asynchronously()
    {
        var source = new CloseRecordingStream();
        var stream = new CleanupRecordingSubStream(source);

        await stream.DisposeAsync();

        Assert.AreEqual(false, stream.Disposing);
        Assert.IsTrue(source.AsyncClosed);
        Assert.IsFalse(source.SyncClosed);
    }
}
