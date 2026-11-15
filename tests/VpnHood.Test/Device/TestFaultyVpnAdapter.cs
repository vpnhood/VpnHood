using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

// A null adapter whose start, reads and writes fail on demand, and whose reader and sender can be held
// at a barrier, for the adapter's own recovery and its stop
public class TestFaultyVpnAdapter(bool autoRestart, TimeSpan? maxPacketSendDelay = null)
    : NullVpnAdapter(new VpnAdapterSettings {
        AdapterName = "VpnHoodFaultyAdapter",
        Blocking = false,
        AutoRestart = autoRestart,
        MaxPacketSendDelay = maxPacketSendDelay ?? TimeSpan.FromMilliseconds(500)
    })
{
    private readonly ManualResetEventSlim _readerRelease = new(false);
    private readonly ManualResetEventSlim _senderRelease = new(false);
    private readonly ManualResetEventSlim _heldPacketRelease = new(false);
    private readonly ManualResetEventSlim _readRelease = new(false);
    private readonly ManualResetEventSlim _interfaceCheckRelease = new(false);
    private readonly TaskCompletionSource _startRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public volatile bool HoldBeforeAdd; // the start holds before it adds the adapter until ReleaseStart
    public volatile bool IsStartHeld;
    public volatile bool FailStart;
    public volatile bool FailRead;
    public volatile bool FailWrite;
    public volatile IpPacket? FailingPacket; // the one packet whose write fails
    public volatile IpPacket? HeldPacket; // the one packet whose write holds until ReleaseHeldPacket
    public volatile bool IsPacketHeld;
    public volatile bool HoldRead; // the reader's next read holds until ReleaseRead, then gives a packet
    public volatile bool IsReadHeld;
    public volatile bool CheckInterface; // the adapter looks for its interface when the network changes
    public volatile bool HoldInterfaceCheck; // that look holds, under the restart lock, until ReleaseInterfaceCheck
    public volatile bool IsInterfaceCheckHeld;
    public volatile bool FailClose;
    public volatile bool FailAddIpV6; // the interface refuses its IPv6 address
    public volatile bool RefuseWrite; // the interface takes no packet: every write finds it full
    public volatile int ReadBufferSize; // the size of the buffer the reader last asked for
    public volatile bool HoldReader; // the reader's next wait holds until ReleaseReader
    public volatile bool HoldSender; // the next write holds until ReleaseSender
    public volatile bool StopAfterOpen; // the owner's stop overtakes the start, between its open and its reader
    public volatile bool StopBeforeAdd; // the owner's stop overtakes the start before it adds the adapter
    public volatile bool DisposeBeforeAdd; // the owner's dispose does
    public volatile bool StopAtRoute; // the owner's stop overtakes the start in its first route
    public volatile bool IsAdded;
    public volatile bool IsOpen;
    public volatile bool IsReaderHeld;
    public volatile bool IsSenderHeld;
    public volatile bool IsReading;
    public int ReadingCount; // readers running, this run's and any that outlived their stop
    public TimeSpan ReaderWaitTimeout = TimeSpan.FromSeconds(5); // the stop's wait for the reader
    public TimeSpan SenderWaitTimeout = TimeSpan.FromSeconds(5); // the stop's wait for the batch being sent
    public int StartCount; // starts tried
    public int RouteCount; // routes the starts added
    public int OpenCount; // starts completed: IsStarted is true from a start's first step on
    public int WriteCount; // writes tried
    public bool? ReaderEndedAtClose; // what the stop's wait for the reader answered
    public bool? SenderEndedAtClose; // what the stop's wait for the sender answered
    public bool? WasReadingAtClose;

    protected override TimeSpan AutoRestartDelay => TimeSpan.FromMilliseconds(200);

    protected override bool CheckAdapterInterfaceAfterNetworkChanged => CheckInterface;

    // the null adapter has no interface to lose: the look only holds
    protected override bool IsAdapterInterfaceEnabled()
    {
        if (HoldInterfaceCheck) {
            IsInterfaceCheckHeld = true;
            _interfaceCheckRelease.Wait();
            IsInterfaceCheckHeld = false;
        }

        return true;
    }

    protected override async Task AdapterAdd(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref StartCount);
        if (StopBeforeAdd)
            Stop();
        if (DisposeBeforeAdd)
            Dispose();
        if (FailStart)
            throw new IOException("Test: the adapter could not be added.");

        if (HoldBeforeAdd) {
            IsStartHeld = true;
            await _startRelease.Task.WaitAsync(cancellationToken);
            IsStartHeld = false;
        }

        await base.AdapterAdd(cancellationToken);
        IsAdded = true;
    }

    protected override void AdapterRemove()
    {
        base.AdapterRemove();
        IsAdded = false;
    }

    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return FailAddIpV6 && ipNetwork.IsV6
            ? throw new IOException("Test: the adapter refused its IPv6 address.")
            : base.AddAddress(ipNetwork, cancellationToken);
    }

    // as WinTun's: the step runs to its end past a stop
    protected override async Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref RouteCount) == 1 && StopAtRoute)
            Stop();

        await base.AddRoute(ipNetwork, cancellationToken);
    }

    protected override async Task AdapterOpen(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref OpenCount);
        await base.AdapterOpen(cancellationToken);
        IsOpen = true;
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

    protected override IpPacket? ReadPacket(int mtu)
    {
        ReadBufferSize = mtu;
        if (!HoldRead)
            return base.ReadPacket(mtu);

        // one reader's read: a later run's reader beside it reads on
        HoldRead = false;
        IsReadHeld = true;
        _readRelease.Wait();
        IsReadHeld = false;
        return PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.3"), IPAddress.Parse("10.0.0.2"), 53, 1000, new byte[16]);
    }

    protected override int ReadPacket(byte[] buffer)
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
        Interlocked.Increment(ref WriteCount);
        if (FailWrite || ReferenceEquals(ipPacket, FailingPacket))
            throw new IOException("Test: the adapter could not be written.");

        if (ReferenceEquals(ipPacket, HeldPacket)) {
            IsPacketHeld = true;
            _heldPacketRelease.Wait();
            IsPacketHeld = false;
        }

        if (HoldSender) {
            IsSenderHeld = true;
            _senderRelease.Wait();
            IsSenderHeld = false;
        }

        // as a real adapter's: no device to take the packet until the start opens it
        return IsOpen && !RefuseWrite && base.WritePacket(ipPacket);
    }

    // as a platform's adapter that finds, after its start, that it cannot keep what the run's options asked for
    public void FailRun(Exception ex)
    {
        StopOnFailure(ex);
    }

    public void ReleaseStart()
    {
        HoldBeforeAdd = false;
        _startRelease.TrySetResult();
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

    public void ReleaseHeldPacket()
    {
        HeldPacket = null;
        _heldPacketRelease.Set();
    }

    public void ReleaseRead()
    {
        HoldRead = false;
        _readRelease.Set();
    }

    public void ReleaseInterfaceCheck()
    {
        HoldInterfaceCheck = false;
        _interfaceCheckRelease.Set();
    }

    // as WinTun's: the reader and the batch being sent leave before what they use is freed
    protected override void AdapterClose()
    {
        if (FailClose)
            throw new IOException("Test: the adapter could not be closed.");

        ReaderEndedAtClose = WaitForReader(ReaderWaitTimeout);
        WasReadingAtClose = IsReading;
        SenderEndedAtClose = WaitForSender(SenderWaitTimeout);
        base.AdapterClose();
        IsOpen = false;
    }

    protected override void DisposeManaged()
    {
        _readerRelease.Set();
        _senderRelease.Set();
        _heldPacketRelease.Set();
        _readRelease.Set();
        _interfaceCheckRelease.Set();
        _startRelease.TrySetResult();
        base.DisposeManaged();
    }
}
