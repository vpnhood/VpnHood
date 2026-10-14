namespace VpnHood.Net.TcpStack.Test;

/// <summary>The retransmit buffer on its own: a ring holding the unacknowledged bytes from SndUna on.</summary>
[TestClass]
public sealed class RetransmitBufferTest
{
    [TestMethod]
    public void Append_Trim_CopyFront_AcrossTheEnd()
    {
        var ring = new RetransmitBuffer(10);
        ring.Append([1, 2, 3, 4, 5, 6, 7]); // indices 0..6
        ring.Trim(5); // SndUna moves to the byte 6, at index 5
        Assert.AreEqual(2, ring.Length);

        ring.Append([8, 9, 10, 11, 12, 13]); // indices 7, 8, 9, then 0, 1, 2: the append crosses the end
        Assert.AreEqual(8, ring.Length);
        CollectionAssert.AreEqual(new byte[] { 6, 7, 8, 9, 10, 11 }, ring.CopyFront(6), "an MSS-capped copy across the end");
        CollectionAssert.AreEqual(new byte[] { 6, 7, 8, 9, 10, 11, 12, 13 }, ring.CopyFront(100), "a long copy stops at the length");

        ring.Append([14, 15]); // exactly Free, into the wrapped part
        Assert.AreEqual(0, ring.Free, "full: the probe's guard");
        Assert.Throws<InvalidOperationException>(() => ring.Append([16]));

        ring.Trim(4); // the start is index 9
        CollectionAssert.AreEqual(new byte[] { 10, 11, 12, 13, 14, 15 }, ring.CopyFront(100), "index 9, then 0..4");
        ring.Trim(2); // the trim crosses the end: the start is index 1
        CollectionAssert.AreEqual(new byte[] { 12, 13, 14, 15 }, ring.CopyFront(100));
    }

    /// <summary>The FIN's sequence number is not in the buffer, so an ack can exceed the length by one.</summary>
    [TestMethod]
    public void Trim_PastTheEnd_EmptiesTheBuffer()
    {
        var ring = new RetransmitBuffer(8);
        ring.Append([1, 2, 3]);
        ring.Trim(4);
        Assert.AreEqual(0, ring.Length);
        Assert.AreEqual(0, ring.CopyFront(8).Length);

        ring.Append([4, 5, 6, 7, 8, 9, 10, 11]); // the whole capacity fits again
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7, 8, 9, 10, 11 }, ring.CopyFront(8));
    }

    [TestMethod]
    public void Append_BeyondFree_ChangesNothing()
    {
        var ring = new RetransmitBuffer(4);
        ring.Append([1, 2, 3]);
        Assert.Throws<InvalidOperationException>(() => ring.Append([4, 5]));
        Assert.AreEqual(3, ring.Length);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ring.CopyFront(4));
    }
}
