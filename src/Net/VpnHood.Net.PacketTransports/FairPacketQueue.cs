using System.Numerics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.PacketTransports;

// A send queue split into lanes by a packet's ports, so a flow that floods fills only its own lane. The busy
// lanes take turns a packet each, and a lane that was empty goes first, every other turn at least: a DNS lookup
// or a call's small packet waits for one packet, not for a backlog. The lanes share the capacity, so one flow
// alone may use all of it, and a full queue drops the oldest packet of its longest lane. A send never waits,
// and the memory is fixed at construction
public abstract class FairPacketQueue : IDisposable
{
    private const int LaneCount = 64;
    private const int LaneMask = LaneCount - 1;
    private const int NoSlot = -1;
    private readonly Lock _lock = new();

    // the queued packets, linked oldest first per lane through _nextSlots; the free slots are linked alike
    private readonly IpPacket?[] _slots;
    private readonly int[] _nextSlots;
    private readonly int[] _laneHeads = new int[LaneCount];
    private readonly int[] _laneTails = new int[LaneCount];
    private readonly int[] _laneCounts = new int[LaneCount];
    private int _freeSlot;
    private int _count;
    private ulong _busyLanes;

    // busy lanes that were empty when their oldest packet came, and have had no turn since
    private ulong _newLanes;
    private bool _isNewLaneTurn;
    private int _nextLane;
    private bool _isWaiting;
    private bool _disposed;
    private long _droppedPackets;

    // rung when a packet comes to the queue its loop waits on, and closed on disposal; a reused waiter, so a
    // wait allocates nothing
    private readonly Channel<bool> _packetSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    public int QueueLength => Volatile.Read(ref _count);
    public long DroppedPackets => Interlocked.Read(ref _droppedPackets);
    protected virtual string Name => VhLogger.FormatType(this);

    protected FairPacketQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _slots = new IpPacket?[capacity];
        _nextSlots = new int[capacity];
        for (var slot = 0; slot < capacity; slot++)
            _nextSlots[slot] = slot + 1 < capacity ? slot + 1 : NoSlot;

        Task.Run(SendPacketsAsync);
    }

    // A packet's turn: the step sends the packet on or disposes it, and must not throw once it has handed it on,
    // as a throw drops the packet. The next turn waits for the step
    protected abstract ValueTask SendQueuedPacketAsync(IpPacket ipPacket);

    // Queues the packet, never waiting: a full queue makes room in its longest lane. A disposed queue refuses
    // it, and the packet is dropped
    public bool SendPacketQueued(IpPacket ipPacket)
    {
        var (isQueued, droppedPacket, isWaiting) = Enqueue(GetLane(ipPacket), ipPacket);
        if (!isQueued) {
            DropPacket(ipPacket, null, "Dropping a packet. Send queue is disposed.");
            return false;
        }

        if (isWaiting)
            _packetSignal.Writer.TryWrite(true);

        if (droppedPacket != null)
            DropPacket(droppedPacket, null, "Dropping the oldest packet of the longest lane. Send queue is full.");

        return true;
    }

    // The lane of a packet's flow: the low bytes of its two ports XORed, so the ephemeral port picks it either
    // way, as the source on upload and the destination on download. Fragments, which may lack the ports, and
    // other protocols share lane 0. It reads only the header, which the packet checked when it was built
    private static int GetLane(IpPacket ipPacket)
    {
        var span = ipPacket.Buffer.Span;
        var isIpV4 = span[0] >> 4 == 4;
        var isFragment = isIpV4 && ((span[6] & 0x3F) | span[7]) != 0; // more fragments, or an offset
        var protocol = (IpProtocol)(isIpV4 ? span[9] : span[6]);
        var portsOffset = isIpV4 ? (span[0] & 0x0F) << 2 : 40;
        return !isFragment && (protocol is IpProtocol.Tcp or IpProtocol.Udp) && span.Length >= portsOffset + 4
            ? (span[portsOffset + 1] ^ span[portsOffset + 3]) & LaneMask
            : 0;
    }

    private EnqueueResult Enqueue(int lane, IpPacket ipPacket)
    {
        lock (_lock) {
            if (_disposed)
                return new EnqueueResult(IsQueued: false, DroppedPacket: null, IsWaiting: false);

            var droppedPacket = _count == _slots.Length ? Unlink(GetLongestLane(lane)) : null;
            Link(lane, ipPacket);
            var isWaiting = _isWaiting;
            _isWaiting = false;
            return new EnqueueResult(IsQueued: true, droppedPacket, isWaiting);
        }
    }

    // the next packet in turn; none once disposed, or when empty, which marks the loop as waiting
    private IpPacket? TakePacket()
    {
        lock (_lock) {
            if (_disposed)
                return null;

            if (_count > 0)
                return Unlink(TakeTurn());

            _isWaiting = true;
            return null;
        }
    }

    // under the lock, with a free slot
    private void Link(int lane, IpPacket ipPacket)
    {
        var laneBit = 1UL << lane;
        var slot = _freeSlot;
        _freeSlot = _nextSlots[slot];
        _slots[slot] = ipPacket;
        _nextSlots[slot] = NoSlot;
        if ((_busyLanes & laneBit) == 0) {
            _laneHeads[lane] = slot;
            _busyLanes |= laneBit;
            _newLanes |= laneBit;
        }
        else {
            _nextSlots[_laneTails[lane]] = slot;
        }

        _laneTails[lane] = slot;
        _laneCounts[lane]++;
        _count++;
    }

    // under the lock, from a busy lane: its oldest packet, whose slot is freed
    private IpPacket Unlink(int lane)
    {
        var slot = _laneHeads[lane];
        var ipPacket = _slots[slot] ?? throw new InvalidOperationException("A queued slot holds no packet.");
        _slots[slot] = null;
        _laneHeads[lane] = _nextSlots[slot];
        if (--_laneCounts[lane] == 0) {
            _busyLanes &= ~(1UL << lane);
            _newLanes &= ~(1UL << lane);
        }

        _nextSlots[slot] = _freeSlot;
        _freeSlot = slot;
        _count--;
        return ipPacket;
    }

    // under the lock, with a packet queued: the lane holding the most, so a flood pays for itself. A tie goes
    // to the arriving packet's lane, then to the first lane from the turn
    private int GetLongestLane(int arrivingLane)
    {
        var longestLane = arrivingLane;
        for (var lanes = BitOperations.RotateRight(_busyLanes, _nextLane); lanes != 0; lanes &= lanes - 1) {
            var lane = (_nextLane + BitOperations.TrailingZeroCount(lanes)) & LaneMask;
            if (_laneCounts[lane] > _laneCounts[longestLane])
                longestLane = lane;
        }

        return longestLane;
    }

    // under the lock, with a packet queued. A new lane goes first, but every other turn at most while older
    // lanes wait, so a lane that keeps emptying cannot starve them
    private int TakeTurn()
    {
        var oldLanes = _busyLanes & ~_newLanes;
        _isNewLaneTurn = _newLanes != 0 && (!_isNewLaneTurn || oldLanes == 0);
        return _isNewLaneTurn ? TakeNewLane() : TakeOldLane(oldLanes);
    }

    // under the lock: the new lane's turn, after which it is an old one while it stays busy
    private int TakeNewLane()
    {
        var lane = BitOperations.TrailingZeroCount(_newLanes);
        _newLanes &= ~(1UL << lane);
        return lane;
    }

    // under the lock: the first old lane from the turn, which then moves past it
    private int TakeOldLane(ulong oldLanes)
    {
        var lane = (_nextLane + BitOperations.TrailingZeroCount(BitOperations.RotateRight(oldLanes, _nextLane))) &
                   LaneMask;
        _nextLane = (lane + 1) & LaneMask;
        return lane;
    }

    private async Task SendPacketsAsync()
    {
        try {
            while (true) {
                var ipPacket = TakePacket();
                if (ipPacket != null)
                    try {
                        await SendQueuedPacketAsync(ipPacket).Vhc();
                    }
                    catch (Exception ex) {
                        // a step that throws did not hand the packet on
                        DropPacket(ipPacket, ex, "Dropping a packet. Its send failed.");
                    }
                else if (await _packetSignal.Reader.WaitToReadAsync().Vhc())
                    _packetSignal.Reader.TryRead(out _);
                else
                    return; // disposed
            }
        }
        catch (Exception ex) {
            // a loop that cannot go on leaves the queue disposed, so its owner sees its packets refused
            VhLogger.Instance.LogError(ex, "Error in the send loop of a fair packet queue. Type: {Type}",
                VhLogger.FormatType(this));
            Dispose();
        }
    }

    // a packet that goes to no one: counted and disposed, even when the logger fails
    private void DropPacket(IpPacket ipPacket, Exception? exception, string message)
    {
        Interlocked.Increment(ref _droppedPackets);
        try {
            LogPacket(ipPacket, exception, message);
        }
        catch {
            // a failing logger must fail neither a send whose packet was queued nor the loop
        }

        ipPacket.Dispose();
    }

    private void LogPacket(IpPacket ipPacket, Exception? exception, string message)
    {
        // as in PacketTransportBase: only at Trace, as it may run for every packet
        if (VhLogger.MinLogLevel > LogLevel.Trace)
            return;

        VhLogger.Instance.Log(exception is null ? LogLevel.Debug : LogLevel.Error,
            message: $"{Name}: {message} {ipPacket}", exception: exception);
    }

    // drops what the queue holds and refuses later sends; a packet in its step is the step's
    public void Dispose()
    {
        var ipPackets = Close();
        _packetSignal.Writer.TryComplete();
        foreach (var ipPacket in ipPackets)
            DropPacket(ipPacket, null, "Dropping a packet. Send queue is disposed.");

        GC.SuppressFinalize(this);
    }

    // marks the queue disposed and takes the packets it holds; a second call takes none
    private List<IpPacket> Close()
    {
        lock (_lock) {
            var ipPackets = new List<IpPacket>(_count);
            _disposed = true;
            while (_count > 0)
                ipPackets.Add(Unlink(BitOperations.TrailingZeroCount(_busyLanes)));

            return ipPackets;
        }
    }

    // Enqueue's result; a struct, as it is made for every packet
    private readonly record struct EnqueueResult(bool IsQueued, IpPacket? DroppedPacket, bool IsWaiting);
}
