using System.Buffers;

namespace VpnHood.Net.TcpStack.Test;

/// <summary>The receive window on its own: the unread count against ReceiveWindowSize, and the window-update rule.</summary>
[TestClass]
public sealed class ReceiveWindowTest
{
    private static ReceiveWindow Create(int windowSize, ushort mss = 1000)
    {
        var options = new LocalTcpStackOptions { ReceiveWindowSize = windowSize };
        return new ReceiveWindow(options, options.CreatePipeOptions(), mss);
    }

    [TestMethod]
    public void Write_ShrinksTheWindow_AndAdvertiseRecordsIt()
    {
        var window = Create(16000);
        Assert.AreEqual((ushort)16000, window.Available);
        Assert.AreEqual((ushort)16000, window.LastAdvertised, "the initial advertisement is the whole window");

        window.Write(new byte[6000]);
        Assert.AreEqual(6000, window.Unread);
        Assert.AreEqual((ushort)10000, window.Available);
        Assert.AreEqual((ushort)10000, window.Advertise());
        Assert.AreEqual((ushort)10000, window.LastAdvertised);

        window.Write(new byte[10000]);
        Assert.AreEqual((ushort)0, window.Available, "a full pipe closes the window");
        Assert.AreEqual((ushort)0, window.Advertise());
    }

    /// <summary>A pipe that fills is a closed window even when nothing advertised the zero: the drain to the floor
    /// re-advertises, while the quarter rule (4000 here) cannot fire against the last advertisement of 16000.</summary>
    [TestMethod]
    public void Write_ClosesTheWindow_WithoutAnAdvertisement()
    {
        var window = Create(16000);
        window.Write(new byte[16000]);
        Assert.IsTrue(window.Consume(4096));
    }

    [TestMethod]
    public void Write_PastTheWindow_Throws()
    {
        var window = Create(16000);
        window.Write(new byte[16000]);
        Assert.Throws<InvalidOperationException>(() => window.Write(new byte[1]));
    }

    /// <summary>A 32000-byte window: the quarter threshold is 8000, so the 4096 floor is what reopens a closed window,
    /// and once.</summary>
    [TestMethod]
    public void Consume_ReopensAClosedWindow_AtTheFloor_Once()
    {
        var window = Create(32000);
        window.Write(new byte[32000]);
        Assert.AreEqual((ushort)0, window.Advertise());

        Assert.IsFalse(window.Consume(4095), "below the floor of 4096: no update yet");
        Assert.IsTrue(window.Consume(1), "the floor: a window update");
        Assert.AreEqual((ushort)4096, window.Available);
        Assert.AreEqual((ushort)0, window.LastAdvertised, "the update is the connection's to send; Advertise records it");
        Assert.IsFalse(window.Consume(1), "one update per reopening");
    }

    /// <summary>A 16000-byte window: the quarter threshold is 4000, above the 1000-byte MSS.</summary>
    [TestMethod]
    public void Consume_UpdatesOnceTheWindowOpenedByAQuarter()
    {
        var window = Create(16000);
        window.Write(new byte[8000]);
        Assert.AreEqual((ushort)8000, window.Advertise());

        Assert.IsFalse(window.Consume(3999), "opened by less than a quarter since the last advertisement");
        Assert.IsTrue(window.Consume(1), "opened by a quarter");
        Assert.AreEqual((ushort)12000, window.Available);
    }

    /// <summary>A 2000-byte window: a quarter is 500, so the 1000-byte MSS is the threshold.</summary>
    [TestMethod]
    public void Consume_ThresholdIsNeverBelowOneMss()
    {
        var window = Create(2000, mss: 1000);
        window.Write(new byte[1500]);
        Assert.AreEqual((ushort)500, window.Advertise());

        Assert.IsFalse(window.Consume(999));
        Assert.IsTrue(window.Consume(1));
    }

    /// <summary>A 2000-byte window with the 1460 MSS: the floor is clamped to the window, and a closed window stays
    /// closed through an update the threshold fired below the floor.</summary>
    [TestMethod]
    public void Consume_FloorIsClampedToTheWindow_AndTheClosedFlagOutlivesAThresholdUpdate()
    {
        var window = Create(2000, mss: 1460);
        window.Write(new byte[2000]);
        Assert.AreEqual((ushort)0, window.Advertise());

        Assert.IsTrue(window.Consume(1460), "the threshold fires below the floor");
        Assert.AreEqual((ushort)1460, window.Advertise());
        Assert.IsTrue(window.Consume(540), "the floor, clamped to 2000, with the window still closed; 540 is under the threshold");
    }

    [TestMethod]
    public void Consume_ClampsToTheUnreadBytes()
    {
        var window = Create(16000);
        window.Write(new byte[5000]);
        window.Consume(long.MaxValue);
        Assert.AreEqual(0, window.Unread);
        Assert.AreEqual((ushort)16000, window.Available);
    }

    [TestMethod]
    public async Task ClearUnread_KeepsTheBytes_AndComplete_EndsTheWriter()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var window = Create(16000);
        window.Write([1, 2, 3]);
        Assert.AreEqual(3, window.ClearUnread());
        Assert.AreEqual(0, window.Unread);
        Assert.AreEqual((ushort)16000, window.Available);

        window.Complete();
        window.Write(new byte[100]);
        Assert.AreEqual(0, window.Unread, "nothing is taken after the completion");

        var result = await window.Reader.ReadAsync(cts.Token);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Buffer.ToArray(), "the bytes stay in the pipe");
        Assert.IsTrue(result.IsCompleted);
        window.Reader.AdvanceTo(result.Buffer.End);
    }
}
