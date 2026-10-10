using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A passthrough transport that records the UDP source port of each packet it sends, in order
public class TestPassthroughPacketTransport : PassthroughPacketTransport
{
    private readonly List<int> _sentPorts = [];
    private int _sendsStarted;

    // set, a packet from this UDP source port is refused before it is handed on
    public volatile int FailPort;

    // set, a send past its disposed check waits here before it sends
    public ManualResetEventSlim? SendGate { get; set; }

    // the sends past their disposed check
    public int SendsStarted => Volatile.Read(ref _sendsStarted);

    public IReadOnlyList<int> SentPorts {
        get {
            lock (_sentPorts)
                return _sentPorts.ToArray();
        }
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        var sourcePort = ipPacket.ExtractUdp().SourcePort;
        if (sourcePort == FailPort)
            throw new InvalidOperationException("Test: no one takes this packet.");

        lock (_sentPorts)
            _sentPorts.Add(sourcePort);
    }

    // a send logs this right after its disposed check
    protected override void LogPacket(IpPacket ipPacket, LogLevel logLevel, Exception? exception,
        string message, params object?[] args)
    {
        if (message == "Sending a packet to queue.") {
            Interlocked.Increment(ref _sendsStarted);
            SendGate?.Wait();
        }

        base.LogPacket(ipPacket, logLevel, exception, message, args);
    }
}
