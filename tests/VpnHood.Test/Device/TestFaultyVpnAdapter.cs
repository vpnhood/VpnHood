using VpnHood.Net.Packets;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

// A null adapter whose start, reads and writes fail on demand, and whose reader and sender can be held
// at a barrier, for the adapter's own recovery and its stop
public class TestFaultyVpnAdapter(bool autoRestart)
    : NullVpnAdapter(new VpnAdapterSettings {
        AdapterName = "VpnHoodFaultyAdapter",
        Blocking = false,
        AutoDisposePackets = true,
        AutoRestart = autoRestart
    })
{
    private readonly ManualResetEventSlim _readerRelease = new(false);
    private readonly ManualResetEventSlim _senderRelease = new(false);
    public volatile bool FailStart;
    public volatile bool FailRead;
    public volatile bool FailWrite;
    public volatile bool HoldReader; // the reader's next wait holds until ReleaseReader
    public volatile bool HoldSender; // the next write holds until ReleaseSender
    public volatile bool StopAfterOpen; // the owner's stop overtakes the start, between its open and its reader
    public volatile bool IsReaderHeld;
    public volatile bool IsSenderHeld;
    public volatile bool IsReading;
    public int ReadingCount; // readers running, this run's and any that outlived their stop
    public TimeSpan ReaderWaitTimeout = TimeSpan.FromSeconds(5); // the stop's wait for the reader
    public int StartCount; // starts tried
    public int OpenCount; // starts completed: IsStarted is true from a start's first step on
    public bool? ReaderEndedAtClose; // what the stop's wait for the reader answered
    public bool? SenderEndedAtClose; // what the stop's wait for the sender answered
    public bool? WasReadingAtClose;

    protected override TimeSpan AutoRestartDelay => TimeSpan.FromMilliseconds(200);

    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref StartCount);
        return FailStart
            ? throw new IOException("Test: the adapter could not be added.")
            : base.AdapterAdd(cancellationToken);
    }

    protected override async Task AdapterOpen(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref OpenCount);
        await base.AdapterOpen(cancellationToken);
        if (StopAfterOpen)
            Stop();
    }

    protected override void StartReadingPackets()
    {
        Interlocked.Increment(ref ReadingCount);
        IsReading = true;
        base.StartReadingPackets();
        IsReading = false;
        Interlocked.Decrement(ref ReadingCount);
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        return FailRead
            ? throw new IOException("Test: the adapter could not be read.")
            : base.ReadPacket(buffer);
    }

    // held at the barrier, or polling instead of blocking, so a read failure set later is seen
    protected override void WaitForTunRead()
    {
        if (!HoldReader) {
            Thread.Sleep(20);
            return;
        }

        IsReaderHeld = true;
        _readerRelease.Wait();
        IsReaderHeld = false;
    }

    protected override bool WritePacket(IpPacket ipPacket)
    {
        if (FailWrite)
            throw new IOException("Test: the adapter could not be written.");

        if (HoldSender) {
            IsSenderHeld = true;
            _senderRelease.Wait();
            IsSenderHeld = false;
        }

        return base.WritePacket(ipPacket);
    }

    public void ReleaseReader()
    {
        HoldReader = false;
        _readerRelease.Set();
    }

    public void ReleaseSender()
    {
        HoldSender = false;
        _senderRelease.Set();
    }

    // as WinTun's: the reader and the batch being sent leave before what they use is freed
    protected override void AdapterClose()
    {
        ReaderEndedAtClose = WaitForReader(ReaderWaitTimeout);
        WasReadingAtClose = IsReading;
        SenderEndedAtClose = WaitForSender(TimeSpan.FromSeconds(5));
        base.AdapterClose();
    }

    protected override void DisposeManaged()
    {
        _readerRelease.Set();
        _senderRelease.Set();
        base.DisposeManaged();
    }
}
