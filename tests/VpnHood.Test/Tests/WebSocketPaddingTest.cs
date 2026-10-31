using System.Buffers.Binary;
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
    public async Task Preserved_payload_is_restored_when_an_unpadded_write_completes_or_fails(bool cancel)
    {
        using var source = new BlockingWriteStream(block: cancel);
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
        CollectionAssert.AreEqual(data, buffer[writer.PreserveWriteBufferLength..]);
        await writer.DisposeAsync();
    }

    [TestMethod]
    public async Task Large_writes_are_not_copied_into_padding_and_expired_padding_stays_off()
    {
        using var source = new RecordingStream([0x82, 0]);
        var writer = new WebSocketStream(source, "bounded", false, false, useStandardMasking: true);
        writer.EnableInitialPadding();
        var large = new byte[writer.PreserveWriteBufferLength + 65536];
        await writer.WritePreservedAsync(large, TestCt);
        Assert.AreEqual(1, (await DecodeFrames(source.Snapshot().Single())).Count);
        await Task.Delay(2100, TestCt);
        await writer.WriteAsync(new byte[] { 1 }, TestCt);
        Assert.AreEqual(1, (await DecodeFrames(source.Snapshot()[1])).Count);
        await writer.DisposeAsync();
    }

    [TestMethod]
    public async Task A_slow_destination_does_not_consume_padding_before_the_first_response()
    {
        using var source = new RecordingStream([0x82, 0]);
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
        using var source = new RecordingStream([0x82, 0]);
        var writer = new WebSocketStream(source, "padding", false, isServer, useStandardMasking: true);
        writer.EnableInitialPadding();
        var data = Encoding.ASCII.GetBytes("the inner TLS hello and the proxy request");
        if (preserved) {
            var buffer = new byte[writer.PreserveWriteBufferLength + data.Length];
            data.CopyTo(buffer, writer.PreserveWriteBufferLength);
            await writer.WritePreservedAsync(buffer, TestCt);
            CollectionAssert.AreEqual(data, buffer[writer.PreserveWriteBufferLength..],
                "Framing must preserve the caller's payload after the write.");
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

        using var readerSource = new RecordingStream(writes.SelectMany(x => x).ToArray());
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
        using var source = new RecordingStream([0x82, 0]);
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
        using var source = new RecordingStream([0x82, 0, 0x82, 0, 0x82, 0]);
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
        using var source = new RecordingStream(input);
        var stream = new WebSocketStream(source, "fragments", false, isServer, useStandardMasking: true);
        var received = new byte[8];
        await stream.ReadExactlyAsync(received, TestCt);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("abcdefgh"), received);
        Assert.AreEqual(0, await stream.ReadAsync(new byte[1], TestCt));
        var reply = await DecodeFrames(source.Snapshot().Single());
        Assert.IsTrue(reply.Single().Header.IsPong);
        Assert.AreEqual(!isServer, !reply.Single().Header.MaskKey.IsEmpty);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("ping"), reply.Single().Payload);
        await stream.DisposeAsync();
    }

    [TestMethod]
    public async Task Close_status_is_unmasked_and_prevents_reuse()
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(payload, 1000);
        using var source = new RecordingStream(Frame(8, true, Encoding.Latin1.GetString(payload), masked: true));
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
        using var source = new RecordingStream(Frame((byte)opcode, final, new string('x', length), masked: false));
        var stream = new WebSocketStream(source, "invalid", false, false, useStandardMasking: true);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => stream.ReadAsync(new byte[1], TestCt).AsTask());
        Assert.IsFalse(stream.CanReuse);
        await stream.DisposeAsync();
    }

    [TestMethod]
    public async Task Legacy_mask_headers_preserve_the_old_unmasked_payload()
    {
        using var source = new RecordingStream([0x82, 0]);
        var writer = new WebSocketStream(source, "legacy", false, false);
        var data = Encoding.ASCII.GetBytes("legacy data");
        await writer.WriteAsync(data, TestCt);
        await writer.DisposeAsync();
        using var readerSource = new RecordingStream(source.Snapshot().SelectMany(x => x).ToArray());
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
        using var source = new BlockingWriteStream();
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
        using var source = new BlockingWriteStream(allowedWrites: 1);
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
    public async Task Connection_owner_stops_cover_when_it_closes_without_reuse(bool quickDispose)
    {
        using var source = new BlockingWriteStream(allowedWrites: 1);
        var writer = new WebSocketStream(source, "owner-close", false, true, useStandardMasking: true);
        await using var connection = new ReusableStreamConnection(new TestStreamConnection(source), writer, null);
        writer.EnableInitialPadding();
        await writer.WriteAsync(new byte[] { 1 }, TestCt);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        if (quickDispose)
            connection.Dispose();
        else
            await connection.DisposeAsync();
        await source.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
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

    private sealed class BlockingWriteStream(bool block = true, int allowedWrites = 0)
        : StreamDecoratorAsync(new MemoryStream(new byte[] { 0x82, 0 }), false)
    {
        private int _writeCount;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!block || ++_writeCount <= allowedWrites)
                return;
            Started.TrySetResult();
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally {
                Finished.TrySetResult();
            }
        }
    }
}
