using System.Buffers.Binary;
using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A fair queue whose turns wait at a gate until let through, a few or all: a queue whose connection stopped
// moving. A turn records the packet's UDP source port, then disposes the packet. It reads the port from the bytes, as
// a test's fragments are whole UDP packets marked as fragments, from which no UDP packet is extracted
public class TestHeldFairPacketQueue(int capacity) : FairPacketQueue(capacity)
{
    private readonly SemaphoreSlim _turnGate = new(0);
    private readonly List<int> _sentPorts = [];
    private volatile bool _isReleased;
    private int _turnsStarted;

    // set, the turn of a packet from this UDP source port throws before it hands the packet on
    public volatile int FailSendPort;

    // the turns begun, held ones among them
    public int TurnsStarted => Volatile.Read(ref _turnsStarted);

    // the UDP source ports of the packets sent, in order
    public IReadOnlyList<int> SentPorts {
        get {
            lock (_sentPorts)
                return _sentPorts.ToArray();
        }
    }

    protected override async ValueTask SendQueuedPacketAsync(IpPacket ipPacket)
    {
        Interlocked.Increment(ref _turnsStarted);
        if (!_isReleased)
            await _turnGate.WaitAsync();

        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(ipPacket.Payload.Span);
        if (sourcePort == FailSendPort)
            throw new InvalidOperationException("Test: the send failed.");

        lock (_sentPorts)
            _sentPorts.Add(sourcePort);

        ipPacket.Dispose();
    }

    // lets this many held turns through
    public void AllowTurns(int count)
    {
        _turnGate.Release(count);
    }

    // lets every turn through from now on
    public void Release()
    {
        _isReleased = true;
        _turnGate.Release();
    }
}
