using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A transport whose batches are held at a barrier until Release: a queue whose connection stopped moving
public class TestHeldPacketTransport(int queueCapacity, bool blocking = false, bool autoDisposePackets = true)
    : PacketTransport(new PacketTransportOptions {
        QueueCapacity = queueCapacity,
        AutoDisposePackets = autoDisposePackets,
        Blocking = blocking
    })
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<int> _sentPorts = [];
    private int _sendsStarted;
    private int _preDisposeCount;
    public volatile bool IsBatchHeld;

    // set, logging a packet the queue drops throws, as a failing logger would
    public volatile bool FailDropLog;
    public int PreDisposeCount => Volatile.Read(ref _preDisposeCount);

    // set, a send past its disposed check waits here before it writes to the queue
    public ManualResetEventSlim? SendGate { get; set; }

    // the sends past their disposed check
    public int SendsStarted => Volatile.Read(ref _sendsStarted);

    // the UDP source ports of the packets sent, in order
    public IReadOnlyList<int> SentPorts {
        get {
            lock (_sentPorts)
                return _sentPorts.ToArray();
        }
    }

    protected override async ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        IsBatchHeld = !_release.Task.IsCompleted;
        await _release.Task;
        IsBatchHeld = false;
        lock (_sentPorts)
            foreach (var ipPacket in ipPackets)
                _sentPorts.Add(ipPacket.ExtractUdp().SourcePort);
    }

    // a send logs this right after its disposed check, before it writes to the queue
    protected override void LogPacket(IpPacket ipPacket, LogLevel logLevel, Exception? exception,
        string message, params object?[] args)
    {
        if (message == "Sending a packet to queue.") {
            Interlocked.Increment(ref _sendsStarted);
            SendGate?.Wait();
        }

        if (FailDropLog && message.StartsWith("Dropping the oldest packet", StringComparison.Ordinal))
            throw new InvalidOperationException("Test: the logger failed.");

        base.LogPacket(ipPacket, logLevel, exception, message, args);
    }

    public void Release()
    {
        _release.TrySetResult();
    }

    protected override void PreDispose()
    {
        Interlocked.Increment(ref _preDisposeCount);
        base.PreDispose();
    }

    protected override void DisposeManaged()
    {
        _release.TrySetResult();
        base.DisposeManaged();
    }
}
