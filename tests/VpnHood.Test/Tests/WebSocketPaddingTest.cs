using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class WebSocketPaddingTest
{
    public required TestContext TestContext { get; set; }
    private CancellationToken TestCt => TestContext.CancellationToken;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Preserved_payload_keeps_the_written_mask_on_completion_or_failure(bool cancel)
    {
        await using var source = new BlockingWriteStream(block: cancel);
        var writer = new WebSocketStream(source, "preserved", false, false, useStandardMasking: true);
        var data = Encoding.ASCII.GetBytes("caller-owned payload");
        var buffer = new byte[writer.PreserveWriteBufferLength + data.Length];
        data.CopyTo(buffer, writer.PreserveWriteBufferLength);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var write = writer.WritePreservedAsync(buffer, cts.Token).AsTask();
        if (cancel) {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            await cts.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => write);
        }
        else {
            await write;
        }
        var lastWrite = source.LastWrite ?? throw new InvalidOperationException("BlockingWriteStream.LastWrite is missing.");
        CollectionAssert.AreEqual(lastWrite[^data.Length..], buffer[writer.PreserveWriteBufferLength..]);
        CollectionAssert.AreEqual(data, (await DecodeFrames(lastWrite)).Single().Payload);
        await writer.DisposeAsync();
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Data_bypasses_the_gate_when_cover_is_absent_or_finished(bool padding, bool preserved)
    {
        await using var source = new RecordingStream([0x82, 0]);
        await using var stream = new WebSocketStream(source, "no-cover-gate", false, true, useStandardMasking: true);
        if (padding) {
            stream.EnableInitialPadding();
            await stream.WriteAsync(new byte[] { 1 }, TestCt);
        }
        var writeGate = GetWriteGate(stream);
        if (padding)
            await GetPaddingCompletion(stream).WaitAsync(TimeSpan.FromSeconds(5), TestCt);

        if (writeGate != null)
            await writeGate.WaitAsync(TestCt);
        try {
            var data = new byte[] { 2, 3, 4 };
            var buffer = new byte[stream.PreserveWriteBufferLength + data.Length];
            data.CopyTo(buffer, stream.PreserveWriteBufferLength);
            var write = preserved
                ? stream.WritePreservedAsync(buffer, TestCt)
                : stream.WriteAsync(data, TestCt);
            await write.AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            var frames = await DecodeFrames(source.Snapshot().Last());
            CollectionAssert.AreEqual(data, frames[0].Payload);
        }
        finally {
            writeGate?.Release();
        }
    }

    [TestMethod]
    public async Task Data_waits_for_cover_and_a_queued_write_survives_cover_completion()
    {
        await using var source = new PausedWriteStream(pausedWrite: 2);
        await using var stream = new WebSocketStream(source, "cover-order", false, true, useStandardMasking: true);
        stream.EnableInitialPadding();
        await stream.WriteAsync(new byte[] { 1 }, TestCt);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        var write = stream.WriteAsync(new byte[] { 2 }, TestCt).AsTask();
        try {
            Assert.IsFalse(write.IsCompleted);
            Assert.AreEqual(2, source.Snapshot().Length);
        }
        finally {
            source.Release.TrySetResult();
        }
        await write.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        await GetPaddingCompletion(stream).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsFalse(source.OverlappedWrites);
        var frames = await DecodeFrames(source.Snapshot().SelectMany(x => x).ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, frames.Where(x => x.Header.IsBinary)
            .SelectMany(x => x.Payload).ToArray());
    }

    [TestMethod]
    public async Task Large_writes_are_not_copied_into_padding_and_expired_padding_stays_off()
    {
        await using var source = new RecordingStream([0x82, 0]);
        var writer = new WebSocketStream(source, "bounded", false, false, useStandardMasking: true);
        writer.EnableInitialPadding();
        var large = new byte[writer.PreserveWriteBufferLength + 65536];
        await writer.WritePreservedAsync(large, TestCt);
        Assert.AreEqual(1, (await DecodeFrames(source.Snapshot().Single())).Count);
        Assert.IsNull(GetWriteGate(writer), "A stream with no cover task needs no gate.");
        await Task.Delay(2100, TestCt);
        await writer.WriteAsync(new byte[] { 1 }, TestCt);
        Assert.AreEqual(1, (await DecodeFrames(source.Snapshot()[1])).Count);
        Assert.IsNull(GetWriteGate(writer), "Expired padding without a cover task also needs no gate.");
        await writer.DisposeAsync();
    }

    [TestMethod]
    public async Task A_slow_destination_does_not_consume_padding_before_the_first_response()
    {
        await using var source = new RecordingStream([0x82, 0]);
        var writer = new WebSocketStream(source, "slow-connect", false, true, useStandardMasking: true);
        writer.EnableInitialPadding();
        await Task.Delay(2100, TestCt);
        await writer.WriteAsync(new byte[] { 1, 2, 3 }, TestCt);
        Assert.AreEqual(2, (await DecodeFrames(source.Snapshot()[0])).Count,
            "The padding window begins with the first write, after destination connection establishment.");
        await writer.DisposeAsync();
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Padding_and_data_share_a_write_and_round_trip(bool isServer, bool preserved)
    {
        await using var source = new RecordingStream([0x82, 0]);
        var writer = new WebSocketStream(source, "padding", false, isServer, useStandardMasking: true);
        writer.EnableInitialPadding();
        var data = Encoding.ASCII.GetBytes("the inner TLS hello and the proxy request");
        if (preserved) {
            var buffer = new byte[writer.PreserveWriteBufferLength + data.Length];
            data.CopyTo(buffer, writer.PreserveWriteBufferLength);
            await writer.WritePreservedAsync(buffer, TestCt);
        }
        else {
            await writer.WriteAsync(data, TestCt);
        }
        await writer.DisposeAsync();

        var writes = source.Snapshot();
        var first = await DecodeFrames(writes[0]);
        Assert.AreEqual(2, first.Count, "The first source write must contain both real data and padding.");
        Assert.IsTrue(first[0].Header.IsBinary);
        CollectionAssert.AreEqual(data, first[0].Payload);
        Assert.IsTrue(first[1].Header.IsText);
        Assert.IsTrue(first[1].Payload.All(x => x is >= 32 and <= 126));
        Assert.AreEqual(!isServer, !first[1].Header.MaskKey.IsEmpty);

        await using var readerSource = new RecordingStream(writes.SelectMany(x => x).ToArray());
        var reader = new WebSocketStream(readerSource, "reader", false, !isServer, useStandardMasking: true);
        var received = new byte[data.Length];
        // Small reads exercise mask offsets across multiple reads of the same frame.
        for (var i = 0; i < received.Length; i++)
            Assert.AreEqual(1, await reader.ReadAsync(received.AsMemory(i, 1), TestCt));
        Assert.AreEqual(0, await reader.ReadAsync(new byte[1], TestCt));
        CollectionAssert.AreEqual(data, received);
        await reader.DisposeAsync();
    }

    [TestMethod]
    public async Task Idle_gaps_have_bounded_cover_and_then_stop()
    {
        await using var source = new RecordingStream([0x82, 0]);
        var stream = new WebSocketStream(source, "idle", false, true, useStandardMasking: true);
        stream.EnableInitialPadding();
        await stream.WriteAsync(new byte[] { 1 }, TestCt);
        await Task.Delay(1300, TestCt);
        var writes = source.Snapshot();
        Assert.IsTrue(writes.Length is >= 2 and <= 4, "An idle stream gets one to three independent cover frames.");
        foreach (var write in writes.Skip(1)) {
            var frame = (await DecodeFrames(write)).Single();
            Assert.IsTrue(frame.Header.IsText);
            Assert.IsTrue(frame.Payload.All(x => x is >= 32 and <= 126));
        }
        await Task.Delay(400, TestCt);
        Assert.AreEqual(writes.Length, source.Snapshot().Length);
        var frames = await DecodeFrames(writes.SelectMany(x => x).ToArray());
        Assert.IsTrue(frames.Where(x => x.Header.IsText).Sum(x => x.WireLength) <= 12288);
        await stream.DisposeAsync();
    }

    [TestMethod]
    public async Task Budget_ends_and_reused_streams_require_fresh_opt_in()
    {
        await using var source = new RecordingStream([0x82, 0, 0x82, 0, 0x82, 0]);
        var first = new WebSocketStream(source, "first", false, true, useStandardMasking: true);
        first.EnableInitialPadding();
        for (var i = 0; i < 20; i++)
            await first.WriteAsync(new byte[] { 1, 2, 3 }, TestCt);
        await first.DisposeAsync();

        var writes = source.Snapshot();
        var frames = await DecodeFrames(writes.SelectMany(x => x).ToArray());
        var textFrames = frames.Where(x => x.Header.IsText).ToArray();
        Assert.IsTrue(textFrames.Length is >= 2 and <= 8);
        Assert.IsTrue(textFrames.Sum(x => x.WireLength) <= 12288);
        Assert.AreEqual(1, (await DecodeFrames(writes[^2])).Count, "Late data has no padding.");

        var second = (WebSocketStream)await first.CreateReuse();
        var before = source.Snapshot().Length;
        await second.WriteAsync(new byte[] { 4 }, TestCt);
        Assert.AreEqual(1, (await DecodeFrames(source.Snapshot()[before])).Count,
            "A pooled connection can next carry a packet channel, which must stay unpadded.");
        await second.DisposeAsync();

        var third = (WebSocketStream)await second.CreateReuse();
        third.EnableInitialPadding();
        before = source.Snapshot().Length;
        await third.WriteAsync(new byte[] { 5 }, TestCt);
        Assert.AreEqual(2, (await DecodeFrames(source.Snapshot()[before])).Count);
        await third.DisposeAsync();
        before = source.Snapshot().Length;
        await Task.Delay(400, TestCt);
        Assert.AreEqual(before, source.Snapshot().Length, "No gap frame may leak past the logical terminator.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Fragmented_binary_and_text_allow_control_frames_between_fragments(bool isServer)
    {
        var input = new[] {
            Frame(1, false, "ignored", masked: isServer),
            Frame(9, true, "ping", masked: isServer),
            Frame(0, true, "also ignored", masked: isServer),
            Frame(2, false, "abc", masked: isServer),
            Frame(10, true, "pong", masked: isServer),
            Frame(0, false, "de", masked: isServer),
            Frame(0, true, "fgh", masked: isServer),
            Frame(2, true, "", masked: isServer)
        }.SelectMany(x => x).ToArray();
        await using var source = new RecordingStream(input);
        var stream = new WebSocketStream(source, "fragments", false, isServer, useStandardMasking: true);
        var received = new byte[8];
        await stream.ReadExactlyAsync(received, TestCt);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("abcdefgh"), received);
        Assert.AreEqual(0, await stream.ReadAsync(new byte[1], TestCt));
        Assert.AreEqual(0, source.Snapshot().Length, "Ping and pong are discarded without writing replies.");
        await stream.DisposeAsync();
    }

    [TestMethod]
    public async Task Close_status_is_unmasked_and_prevents_reuse()
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(payload, 1000);
        await using var source = new RecordingStream(Frame(8, true, Encoding.Latin1.GetString(payload), masked: true));
        var stream = new WebSocketStream(source, "close", false, true, useStandardMasking: true);
        Assert.AreEqual(0, await stream.ReadAsync(new byte[1], TestCt));
        Assert.AreEqual((ushort)1000, stream.CloseStatus);
        Assert.IsFalse(stream.CanReuse);
        await stream.DisposeAsync();
    }

    [TestMethod]
    [DataRow(9, false, 1)]
    [DataRow(10, true, 126)]
    [DataRow(8, true, 1)]
    [DataRow(0, true, 1)]
    public async Task Invalid_control_or_continuation_frames_fail(int opcode, bool final, int length)
    {
        await using var source = new RecordingStream(Frame((byte)opcode, final, new string('x', length), masked: false));
        var stream = new WebSocketStream(source, "invalid", false, false, useStandardMasking: true);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => stream.ReadAsync(new byte[1], TestCt).AsTask());
        Assert.IsFalse(stream.CanReuse);
        await stream.DisposeAsync();
    }

    [TestMethod]
    public async Task Legacy_mask_headers_preserve_the_old_unmasked_payload()
    {
        await using var source = new RecordingStream([0x82, 0]);
        var writer = new WebSocketStream(source, "legacy", false, false);
        var data = Encoding.ASCII.GetBytes("legacy data");
        await writer.WriteAsync(data, TestCt);
        await writer.DisposeAsync();
        await using var readerSource = new RecordingStream(source.Snapshot().SelectMany(x => x).ToArray());
        var reader = new WebSocketStream(readerSource, "legacy", false, true);
        var received = new byte[data.Length];
        await reader.ReadExactlyAsync(received, TestCt);
        CollectionAssert.AreEqual(data, received);
        await reader.DisposeAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_and_quick_dispose_release_an_inflight_padded_write(bool quickDispose)
    {
        await using var source = new BlockingWriteStream();
        var writer = new WebSocketStream(source, "cancel", false, true, useStandardMasking: true);
        writer.EnableInitialPadding();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var write = writer.WriteAsync(new byte[] { 1, 2, 3 }, cts.Token).AsTask();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        if (quickDispose)
            writer.Dispose();
        else
            await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => write.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsFalse(writer.CanReuse);
        await writer.DisposeAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Closing_cancels_an_inflight_gap_frame_and_prevents_reuse(bool quickDispose)
    {
        await using var source = new BlockingWriteStream(allowedWrites: 1);
        var writer = new WebSocketStream(source, "gap-close", false, true, useStandardMasking: true);
        writer.EnableInitialPadding();
        await writer.WriteAsync(new byte[] { 1 }, TestCt);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        if (quickDispose)
            writer.Dispose();
        else
            await writer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        await source.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        // The cover task marks the stream before its completion is joined by graceful disposal.
        await VpnHood.Net.Toolkit.Utils.VhTestUtil.AssertEqualsWait(false, () => writer.CanReuse,
            cancellationToken: TestCt);
        await writer.DisposeAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cover_failure_stops_data_when_the_transport_owner_closes_without_reuse(bool quickDispose)
    {
        await using var source = new BlockingWriteStream(allowedWrites: 1);
        var writer = new WebSocketStream(source, "owner-close", false, true, useStandardMasking: true);
        await using var connection = new ReusableStreamConnection(new TestStreamConnection(source), writer, null);
        writer.EnableInitialPadding();
        await writer.WriteAsync(new byte[] { 1 }, TestCt);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        var write = writer.WriteAsync(new byte[] { 2 }, TestCt).AsTask();
        if (quickDispose)
            connection.Dispose();
        else
            await connection.DisposeAsync();
        await source.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        await GetPaddingCompletion(writer).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsFalse(writer.CanReuse);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => write.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => writer.WriteAsync(new byte[] { 3 }, TestCt).AsTask());
        Assert.AreEqual(2, source.WriteCount, "No data may follow a potentially partial cover frame.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Close_waits_for_an_inflight_data_frame_without_interleaving(bool graceful)
    {
        await using var source = new PausedWriteStream();
        await using var stream = new WebSocketStream(source, "close-order", false, true, useStandardMasking: true);
        var data = Encoding.ASCII.GetBytes("the complete real frame must precede the close");
        var buffer = new byte[stream.PreserveWriteBufferLength + data.Length];
        data.CopyTo(buffer, stream.PreserveWriteBufferLength);
        var write = stream.WritePreservedAsync(buffer, TestCt).AsTask();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        var close = graceful ? stream.DisposeAsync().AsTask() : stream.WriteClose(1000, TestCt).AsTask();
        try {
            Assert.IsFalse(write.IsCompleted);
            Assert.IsFalse(close.IsCompleted, "Closing cannot overtake a transport write that still owns its buffer.");
            Assert.AreEqual(1, source.Snapshot().Length);
        }
        finally {
            source.Release.TrySetResult();
        }
        await Task.WhenAll(write, close).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        var frames = await DecodeFrames(source.Snapshot().SelectMany(x => x).ToArray());
        Assert.AreEqual(2, frames.Count);
        Assert.IsTrue(frames[0].Header.IsBinary);
        CollectionAssert.AreEqual(data, frames[0].Payload);
        Assert.AreEqual(!graceful, frames[1].Header.IsCloseConnection);
        Assert.AreEqual(graceful ? 0 : 2, frames[1].Payload.Length);
        Assert.AreEqual(graceful ? 2 : 1, stream.WroteChunkCount);
        CollectionAssert.AreEqual(data, buffer[stream.PreserveWriteBufferLength..]);
        Assert.AreEqual(graceful, stream.CanReuse);
    }

    private static object GetFrameWriter(WebSocketStream stream)
    {
        var field = typeof(WebSocketStream).GetField("_frameWriter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WebSocketStream._frameWriter field is missing.");
        return field.GetValue(stream)
            ?? throw new InvalidOperationException("WebSocketStream._frameWriter value is missing.");
    }

    private static SemaphoreSlim? GetWriteGate(WebSocketStream stream)
    {
        var writer = GetFrameWriter(stream);
        var field = writer.GetType().GetField("_writeGate", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WebSocketFrameWriter._writeGate field is missing.");
        return (SemaphoreSlim?)field.GetValue(writer);
    }

    private static Task GetPaddingCompletion(WebSocketStream stream)
    {
        var writer = GetFrameWriter(stream);
        var property = writer.GetType().GetProperty("PaddingCompletion")
            ?? throw new InvalidOperationException("WebSocketFrameWriter.PaddingCompletion property is missing.");
        return (Task)(property.GetValue(writer)
            ?? throw new InvalidOperationException("WebSocketFrameWriter.PaddingCompletion value is missing."));
    }

    private async Task<List<DecodedFrame>> DecodeFrames(byte[] bytes)
    {
        using var source = new MemoryStream(bytes);
        var result = new List<DecodedFrame>();
        while (source.Position < source.Length) {
            var start = source.Position;
            var header = await WebSocketUtils.ReadWebSocketHeader(source, new byte[14], TestCt);
            var payload = new byte[(int)header.PayloadLength];
            await source.ReadExactlyAsync(payload, TestCt);
            if (!header.MaskKey.IsEmpty)
                for (var i = 0; i < payload.Length; i++)
                    payload[i] ^= header.MaskKey.Span[i & 3];
            result.Add(new DecodedFrame(header, payload, (int)(source.Position - start)));
        }
        return result;
    }

    private static byte[] Frame(byte opcode, bool final, string text, bool masked)
    {
        var payload = Encoding.Latin1.GetBytes(text);
        var frame = new byte[payload.Length + 14];
        byte[] key = [7, 19, 101, 233];
        var header = masked
            ? WebSocketUtils.BuildWebSocketFrameHeader(frame, payload.Length, key)
            : WebSocketUtils.BuildWebSocketFrameHeader(frame, payload.Length);
        header[0] = (byte)(opcode | (final ? 0x80 : 0));
        payload.CopyTo(frame, header.Length);
        if (masked)
            for (var i = 0; i < payload.Length; i++)
                frame[header.Length + i] ^= key[i & 3];
        return frame[..(header.Length + payload.Length)];
    }

    private sealed record DecodedFrame(WebSocketHeader Header, byte[] Payload, int WireLength);

    private sealed class RecordingStream(byte[] input) : StreamDecoratorAsync(new MemoryStream(input), false)
    {
        private readonly Lock _lock = new();
        private readonly List<byte[]> _writes = [];

        public byte[][] Snapshot()
        {
            lock (_lock)
                return _writes.ToArray();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
                _writes.Add(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PausedWriteStream(int pausedWrite = 1)
        : StreamDecoratorAsync(new MemoryStream(new byte[] { 0x82, 0 }), false)
    {
        private readonly Lock _lock = new();
        private readonly List<byte[]> _writes = [];
        private int _writeCount;
        private int _activeWrites;
        public bool OverlappedWrites { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[][] Snapshot()
        {
            lock (_lock)
                return _writes.ToArray();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _activeWrites) != 1)
                OverlappedWrites = true;
            try {
                lock (_lock)
                    _writes.Add(buffer.ToArray());
                if (Interlocked.Increment(ref _writeCount) == pausedWrite) {
                    Started.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                }
            }
            finally {
                Interlocked.Decrement(ref _activeWrites);
            }
        }
    }

    private sealed class BlockingWriteStream(bool block = true, int allowedWrites = 0)
        : StreamDecoratorAsync(new MemoryStream(new byte[] { 0x82, 0 }), false)
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[]? LastWrite { get; private set; }
        public int WriteCount => _writeCount;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var writeCount = Interlocked.Increment(ref _writeCount);
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            LastWrite = buffer.ToArray();
            if (writeCount <= allowedWrites || !block)
                return;
            Started.TrySetResult();
            try {
                await _closed.Task.WaitAsync(cancellationToken);
                throw new ObjectDisposedException(nameof(BlockingWriteStream));
            }
            finally {
                Finished.TrySetResult();
            }
        }

        protected override void Dispose(bool disposing)
        {
            _closed.TrySetResult();
            base.Dispose(disposing);
        }
    }
}
