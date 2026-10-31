using VpnHood.Net.Http3;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3StreamReaderTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Buffered_integer_payload_and_discard_reads_preserve_wire_order()
    {
        using var source = new MemoryStream([0x40, 0x42, 1, 2, 3, 4, 5]);
        await using var reader = new Http3StreamReader(source) { ReadAvailableOnly = true };
        Assert.AreEqual(66L, await reader.ReadInteger(TestContext.CancellationToken));
        var bytes = new byte[2];
        Assert.AreEqual(2, await reader.Read(bytes, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, bytes);
        Assert.AreEqual(2, await reader.DiscardSome(2, TestContext.CancellationToken));
        Assert.AreEqual(5, await reader.ReadByte(TestContext.CancellationToken));
        Assert.IsNull(await reader.ReadIntegerOrEnd(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task An_abandoned_read_owns_its_private_buffer_until_disposal_observes_cancellation()
    {
        using var source = new PendingReadStream();
        await using var reader = new Http3StreamReader(source) { ReadAvailableOnly = true };
        byte[] callerBuffer = [0x11, 0x22];
        await Assert.ThrowsExactlyAsync<Http3ReadPendingException>(() =>
            reader.Read(callerBuffer, TestContext.CancellationToken).AsTask());
        Assert.IsFalse(source.ReadCompleted);
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 }, callerBuffer);

        await reader.DisposeAsync();
        Assert.IsTrue(source.CancellationObserved);
        Assert.IsTrue(source.ReadCompleted, "Disposal must observe the pending write before returning its pool buffer.");
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 }, callerBuffer,
            "A late native read must never write into the caller's released temporary buffer.");
    }

    [TestMethod]
    public async Task The_available_budget_counts_integer_payload_and_discard_reads()
    {
        using var source = new MemoryStream([0x40, 0x42, 1, 2, 3, 4, 5]);
        await using var reader = new Http3StreamReader(source) {
            ReadAvailableOnly = true,
            AvailableBytesLimit = 6
        };
        Assert.AreEqual(66L, await reader.ReadInteger(TestContext.CancellationToken));
        Assert.AreEqual(2, await reader.Read(new byte[2], TestContext.CancellationToken));
        Assert.AreEqual(2, await reader.DiscardSome(20, TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<Http3ReadBudgetExceededException>(() =>
            reader.ReadByte(TestContext.CancellationToken).AsTask());
    }

    private sealed class PendingReadStream : MemoryStream
    {
        public bool CancellationObserved { get; private set; }
        public bool ReadCompleted { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(ReadUntilCancellation(buffer, cancellationToken));

        private async Task<int> ReadUntilCancellation(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
            catch (OperationCanceledException) {
                CancellationObserved = true;
                // Model an I/O completion that still touches its destination while cancellation wins.
                buffer.Span[0] = 0x7e;
                throw;
            }
            finally {
                ReadCompleted = true;
            }
        }
    }
}
