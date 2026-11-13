using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A transport whose batches are held at a barrier until Release: a queue whose connection stopped moving
public class TestHeldPacketTransport(int queueCapacity, bool blocking = false, bool autoDisposePackets = true,
    TimeSpan? blockingTimeout = null)
    : PacketTransport(new PacketTransportOptions {
        QueueCapacity = queueCapacity,
        AutoDisposePackets = autoDisposePackets,
        Blocking = blocking,
        BlockingTimeout = blockingTimeout
    })
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<int> _sentPorts = [];
    private readonly List<int> _batchSizes = [];
    private int _sendsStarted;
    private int _disposedPacketLogs;
    private int _preDisposeCount;
    private int _disposeUnmanagedCount;
    public volatile bool IsBatchHeld;

    // set, a batch holding a packet from this UDP source port fails to send
    public volatile int FailSendPort;

    // set, the send gives up a packet from this UDP source port, as a device that stays full does, before the
    // batch goes or fails
    public volatile int DropPort;

    // the log lines written of a packet already disposed: a logger may read the packet it is given
    public int DisposedPacketLogs => Volatile.Read(ref _disposedPacketLogs);

    // the size of each batch the send was given, in order
    public IReadOnlyList<int> BatchSizes {
        get {
            lock (_batchSizes)
                return _batchSizes.ToArray();
        }
    }

    public int PreDisposeCount => Volatile.Read(ref _preDisposeCount);
    public int DisposeUnmanagedCount => Volatile.Read(ref _disposeUnmanagedCount);
    public bool IsTransportDisposed => IsDisposed;

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

    // a packet arriving from the other side
    public void Receive(IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    protected override async ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        IsBatchHeld = !_release.Task.IsCompleted;
        await _release.Task;
        IsBatchHeld = false;
        lock (_batchSizes)
            _batchSizes.Add(ipPackets.Count);

        foreach (var ipPacket in ipPackets)
            if (ipPacket.ExtractUdp().SourcePort == DropPort)
                DropPacket(ipPacket);

        if (ipPackets.Any(ipPacket => ipPacket.ExtractUdp().SourcePort == FailSendPort))
            throw new InvalidOperationException("Test: the send failed.");

        lock (_sentPorts)
            foreach (var ipPacket in ipPackets)
                if (ipPacket.ExtractUdp().SourcePort != DropPort)
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

        try {
            _ = ipPacket.Buffer;
        }
        catch (ObjectDisposedException) {
            Interlocked.Increment(ref _disposedPacketLogs);
        }

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

    protected override void DisposeUnmanaged()
    {
        Interlocked.Increment(ref _disposeUnmanagedCount);
        base.DisposeUnmanaged();
    }
}
