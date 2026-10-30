using System.Buffers;
using VpnHood.Core.Server.ProbeResistance;

namespace VpnHood.Test.Tests.Servers;

[TestClass]
public class HttpReadStreamTest : TestBase
{
    [TestMethod]
    public async Task Http_keeps_its_buffer_for_pipelined_requests()
    {
        var pool = new TrackingPool();
        await using var source = new LimitedReadStream("headnextmore"u8.ToArray(), 8);
        await using var stream = new HttpReadStream(source, pool);

        Assert.IsTrue(await stream.EnsureBuffered(1, TestCt));
        stream.Consume(4);
        Assert.IsTrue(await stream.EnsureBuffered(4, TestCt));
        CollectionAssert.AreEqual("next"u8.ToArray(), stream.BufferedSpan.ToArray());
        stream.Consume(4);
        Assert.IsTrue(await stream.EnsureBuffered(1, TestCt));
        CollectionAssert.AreEqual("more"u8.ToArray(), stream.BufferedSpan.ToArray());
        Assert.AreEqual(1, pool.RentCount);
        Assert.AreEqual(0, pool.ReturnCount);
    }

    [TestMethod]
    public async Task Handoff_returns_the_buffer_after_the_last_read_ahead_byte()
    {
        var pool = new TrackingPool();
        await using var source = new LimitedReadStream("headtailwire"u8.ToArray(), 8);
        await using var stream = new HttpReadStream(source, pool);
        Assert.IsTrue(await stream.EnsureBuffered(1, TestCt));
        stream.Consume(4);
        stream.CompleteHttp();
        Assert.AreEqual(0, pool.ReturnCount);

        var read = new byte[4];
        Assert.AreEqual(2, await stream.ReadAsync(read.AsMemory(0, 2), TestCt));
        CollectionAssert.AreEqual("ta"u8.ToArray(), read[..2]);
        Assert.AreEqual(0, await stream.ReadAsync(Memory<byte>.Empty, TestCt));
        Assert.AreEqual(0, pool.ReturnCount);
        Assert.AreEqual(2, await stream.ReadAsync(read, TestCt));
        CollectionAssert.AreEqual("il"u8.ToArray(), read[..2]);
        Assert.AreEqual(1, pool.ReturnCount);

        Assert.AreEqual(4, await stream.ReadAsync(read, TestCt));
        CollectionAssert.AreEqual("wire"u8.ToArray(), read);
        Assert.AreEqual(0, await stream.ReadAsync(read, TestCt));
        stream.CompleteHttp();
        await stream.DisposeAsync();
        Assert.AreEqual(1, pool.RentCount);
        Assert.AreEqual(1, pool.ReturnCount);
    }

    [TestMethod]
    public async Task Handoff_without_read_ahead_returns_the_buffer_immediately()
    {
        var pool = new TrackingPool();
        await using var source = new LimitedReadStream("headwire"u8.ToArray(), 4);
        await using var stream = new HttpReadStream(source, pool);
        Assert.IsTrue(await stream.EnsureBuffered(1, TestCt));
        stream.Consume(4);
        Assert.AreEqual(0, pool.ReturnCount);

        stream.CompleteHttp();
        Assert.AreEqual(1, pool.ReturnCount);
        var read = new byte[4];
        Assert.AreEqual(4, await stream.ReadAsync(read, TestCt));
        CollectionAssert.AreEqual("wire"u8.ToArray(), read);
        Assert.AreEqual(1, pool.RentCount);
        Assert.ThrowsExactly<InvalidOperationException>(() => stream.EnsureBuffered(1, TestCt));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Disposal_returns_the_buffer_once_and_leaves_the_source_open(bool asyncDispose)
    {
        var pool = new TrackingPool();
        await using var source = new MemoryStream("data"u8.ToArray());
        var stream = new HttpReadStream(source, pool);
        Assert.IsTrue(await stream.EnsureBuffered(1, TestCt));
        if (asyncDispose)
            await stream.DisposeAsync();
        else
            stream.Dispose();

        stream.Dispose();
        await stream.DisposeAsync();
        Assert.AreEqual(1, pool.ReturnCount);
        Assert.IsTrue(source.CanRead);
        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.EnsureBuffered(1, TestCt));
        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.ReadAsync(new byte[1], TestCt));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Disposal_during_a_read_keeps_the_buffer_until_the_source_finishes(bool asyncDispose)
    {
        var pool = new TrackingPool();
        await using var source = new PendingReadStream();
        await using var stream = new HttpReadStream(source, pool);
        var fill = stream.EnsureBuffered(1, TestCt).AsTask();
        await source.ReadStarted.Task.WaitAsync(TestCt);
        try {
            if (asyncDispose)
                await stream.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            else
                await Task.Run(stream.Dispose, TestCt).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            stream.Dispose();
            Assert.AreEqual(0, pool.ReturnCount, "The source still owns the destination memory.");
        }
        finally {
            source.FinishRead.TrySetResult();
        }

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fill);
        Assert.AreEqual(1, pool.ReturnCount);
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_releases_the_read_before_disposal_returns_its_buffer(bool disposeBeforeCancellation)
    {
        var pool = new TrackingPool();
        await using var source = new PendingReadStream();
        await using var stream = new HttpReadStream(source, pool);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var fill = stream.EnsureBuffered(1, cancellation.Token).AsTask();
        await source.ReadStarted.Task.WaitAsync(TestCt);

        if (disposeBeforeCancellation)
            stream.Dispose();
        Assert.AreEqual(0, pool.ReturnCount);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fill);
        Assert.AreEqual(disposeBeforeCancellation ? 1 : 0, pool.ReturnCount);
        await stream.DisposeAsync();
        Assert.AreEqual(1, pool.ReturnCount);
    }

    [TestMethod]
    public async Task Concurrent_disposals_return_the_buffer_once()
    {
        var pool = new TrackingPool();
        await using var source = new MemoryStream("data"u8.ToArray());
        var stream = new HttpReadStream(source, pool);
        Assert.IsTrue(await stream.EnsureBuffered(1, TestCt));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () => {
            if (i % 2 == 0)
                stream.Dispose();
            else
                await stream.DisposeAsync();
        }, TestCt)));
        Assert.AreEqual(1, pool.ReturnCount);
        Assert.IsTrue(source.CanRead);
    }

    private sealed class TrackingPool : ArrayPool<byte>
    {
        private byte[] _rented = [];
        public int RentCount { get; private set; }
        public int ReturnCount { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            Assert.AreEqual(16 * 1024, minimumLength);
            RentCount++;
            _rented = new byte[minimumLength];
            return _rented;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.AreSame(_rented, array);
            Assert.AreEqual(0, ReturnCount, "A rented buffer must be returned exactly once.");
            Assert.IsTrue(clearArray, "The shared pool must not retain HTTP headers or tunnel data.");
            ReturnCount++;
            array.AsSpan().Fill(0xff);
        }
    }

    private sealed class LimitedReadStream(byte[] input, int maxReadSize) : MemoryStream(input)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return base.ReadAsync(buffer[..Math.Min(maxReadSize, buffer.Length)], cancellationToken);
        }
    }

    private sealed class PendingReadStream : MemoryStream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await FinishRead.Task.WaitAsync(cancellationToken);
            buffer.Span[0] = 42;
            return 1;
        }
    }
}
