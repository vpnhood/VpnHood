using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3StreamReaderTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Buffered_integer_payload_and_discard_reads_preserve_wire_order()
    {
        using var source = new MemoryStream([0x40, 0x42, 1, 2, 3, 4, 5]);
        await using var reader = new Http3StreamReader(source);
        Assert.AreEqual(66L, await reader.ReadInteger(TestContext.CancellationToken));
        var bytes = new byte[2];
        Assert.AreEqual(2, await reader.Read(bytes, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, bytes);
        Assert.AreEqual(2, await reader.DiscardSome(2, TestContext.CancellationToken));
        Assert.AreEqual(5, await reader.ReadByte(TestContext.CancellationToken));
        Assert.IsNull(await reader.ReadIntegerOrEnd(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Stopping_inspection_joins_the_native_read_before_releasing_its_buffer()
    {
        using var source = new Http3PendingReadStream();
        await using var reader = new Http3StreamReader(source);
        reader.BeginBodyInspection(64 * 1024);
        byte[] callerBuffer = [0x11, 0x22];
        var read = reader.Read(callerBuffer, TestContext.CancellationToken).AsTask();
        await source.WaitForRead(TestContext.CancellationToken);
        Assert.IsFalse(source.ReadCompleted);
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 }, callerBuffer);

        reader.StopReading();
        await Assert.ThrowsExactlyAsync<Http3InspectionCompleteException>(() => read);
        Assert.IsTrue(source.CancellationObserved);
        Assert.IsTrue(source.ReadCompleted, "Stopping inspection must observe the native read before releasing its buffer.");
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 }, callerBuffer,
            "A late native read must never write into the caller's released temporary buffer.");
    }

    [TestMethod]
    public async Task A_new_pending_read_after_response_submission_stops_without_waiting_for_peer_input()
    {
        using var source = new Http3PendingReadStream();
        var quic = new Http3RecordingQuicStream(Stream.Null);
        await using var reader = new Http3StreamReader(source, quic: quic);
        reader.BeginBodyInspection(64 * 1024);
        reader.ResponseSubmitted();

        await Assert.ThrowsExactlyAsync<Http3InspectionCompleteException>(() =>
            reader.ReadByte(TestContext.CancellationToken).AsTask());

        Assert.IsTrue(source.CancellationObserved);
        Assert.IsTrue(source.ReadCompleted);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
    }

    [TestMethod]
    public async Task A_completed_response_still_inspects_synchronously_available_bytes()
    {
        // The second byte is in a new native read, as a trailer field may be after a full buffer.
        using var source = new MemoryStream(new byte[4097]);
        var quic = new Http3RecordingQuicStream(Stream.Null);
        await using var reader = new Http3StreamReader(source, quic: quic);
        reader.BeginBodyInspection(64 * 1024);
        Assert.AreEqual(4096, await reader.DiscardSome(4096, TestContext.CancellationToken));

        reader.ResponseSubmitted();
        reader.CompleteResponse();

        Assert.AreEqual(0, await reader.ReadByte(TestContext.CancellationToken));
        Assert.AreEqual(-1, await reader.ReadByte(TestContext.CancellationToken));
        reader.FinishInspection();
        Assert.IsNull(quic.ReadAbortCode, "FIN was consumed without a pending native read.");
    }

    [TestMethod]
    public async Task Exhausted_inspection_budget_still_aborts_unfinished_input()
    {
        using var source = new MemoryStream([1, 2]);
        var quic = new Http3RecordingQuicStream(Stream.Null);
        await using var reader = new Http3StreamReader(source, quic: quic);
        reader.BeginBodyInspection(1);
        Assert.AreEqual(1, await reader.ReadByte(TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<Http3ReadBudgetExceededException>(() =>
            reader.ReadByte(TestContext.CancellationToken).AsTask());

        reader.ResponseSubmitted();
        reader.CompleteResponse();
        reader.FinishInspection();

        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
    }

    [TestMethod]
    public async Task A_completed_response_aborts_the_next_pending_native_read()
    {
        using var source = new Http3PendingReadStream();
        var quic = new Http3RecordingQuicStream(Stream.Null);
        await using var reader = new Http3StreamReader(source, quic: quic);
        reader.BeginBodyInspection(64 * 1024);
        reader.ResponseSubmitted();
        reader.CompleteResponse();
        Assert.IsNull(quic.ReadAbortCode, "A completed response must wait for the next native read boundary.");

        await Assert.ThrowsExactlyAsync<Http3InspectionCompleteException>(() =>
            reader.ReadByte(TestContext.CancellationToken).AsTask());

        Assert.IsTrue(source.CancellationObserved);
        Assert.IsTrue(source.ReadCompleted);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
    }

    [TestMethod]
    public async Task Stopping_inspection_translates_cancellation_during_native_read_setup()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var source = new Http3SynchronousReadHoldingStream(lifetime.Token);
        await using var reader = new Http3StreamReader(source);
        reader.BeginBodyInspection(64 * 1024);
        byte[] callerBuffer = [0x11, 0x22];
        var read = Task.Run(() => reader.Read(callerBuffer, lifetime.Token).AsTask(), lifetime.Token);
        try {
            await source.WaitForRead(lifetime.Token);
            reader.StopReading();
            source.ReleaseRead();

            await Assert.ThrowsExactlyAsync<Http3InspectionCompleteException>(() => read);
            CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 }, callerBuffer);
        }
        finally {
            source.ReleaseRead();
            try { await read; }
            catch (Exception) {
                // Observe native completion before the reader and its private buffer are released.
            }
        }
    }

    [TestMethod]
    public async Task The_available_budget_counts_integer_payload_and_discard_reads()
    {
        using var source = new MemoryStream([0x40, 0x42, 1, 2, 3, 4, 5]);
        await using var reader = new Http3StreamReader(source);
        reader.BeginBodyInspection(6);
        Assert.AreEqual(66L, await reader.ReadInteger(TestContext.CancellationToken));
        Assert.AreEqual(2, await reader.Read(new byte[2], TestContext.CancellationToken));
        Assert.AreEqual(2, await reader.DiscardSome(20, TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<Http3ReadBudgetExceededException>(() =>
            reader.ReadByte(TestContext.CancellationToken).AsTask());
    }

    [TestMethod]
    [DataRow(false, 4096)]
    [DataRow(true, 16384)]
    public async Task Only_request_readers_grow_after_consuming_a_full_buffer(bool allowBufferGrowth, int nextReadSize)
    {
        using var source = new Http3ReadSizeRecordingStream(4097);
        await using var reader = new Http3StreamReader(source, allowBufferGrowth);
        await reader.Discard(4097, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { 4096, nextReadSize }, source.ReadSizes.ToArray());
    }

}
