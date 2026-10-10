using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Configuration;
using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Tunneling;

// A tunnel with a send limit. The session's packets wait in its queue, one for all its channels, and leave it at
// the limit: the session's bottleneck, sharing the limit with its proxy streams. The queue holds about a quarter
// second of the limit, at least a new connection's first burst, in lanes by flow that take turns, so a flow that
// floods fills only its own lane
public class ThrottledTunnel : Tunnel
{
    private const int MinQueueCapacity = 32;
    private readonly ThrottleQueue _throttleQueue;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    // the packets the queue dropped: a full queue's oldest, and those it held at the end
    public long QueueDroppedPackets => _throttleQueue.DroppedPackets;

    public ThrottledTunnel(TunnelOptions options)
        : base(options, throttlesPackets: true)
    {
        _cancellationToken = _cancellationTokenSource.Token;
        _throttleQueue = new ThrottleQueue(this, (int)Math.Clamp(
            options.MaxSpeed.Sent / 4 / TransportDefaults.MaxPacketSize,
            MinQueueCapacity, PacketTransportOptions.DefaultQueueCapacity));
    }

    // Hands the packet to the queue, whose turn routes it; so it completes at once and never routes the packet
    // itself, as the base requires. A queue that refuses it while the tunnel lives has lost its loop: the tunnel
    // goes down, so its senders fail loud rather than lose every packet quietly
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        if (!_throttleQueue.SendPacketQueued(ipPackets[0]) && !IsDisposing)
            Dispose();

        return default;
    }

    // A packet's turn: routed, then paid for, the wait holding the next turn. The length is read first, as the
    // channel may send and dispose the packet at once. Once handed on, the packet is the channel's, so nothing
    // after may throw back to the queue, which would drop it
    private ValueTask SendThrottledPacketAsync(IpPacket ipPacket)
    {
        var packetLength = ipPacket.PacketLength;
        if (!SendPacketToChannel(ipPacket))
            return default;

        try {
            var throttleTask = TrafficMeter.ThrottleSendAsync(packetLength, _cancellationToken);
            return throttleTask.IsCompletedSuccessfully ? default : WaitForThrottle(throttleTask);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not pay a sent packet to the speed limit.");
            return default;
        }
    }

    private static async ValueTask WaitForThrottle(ValueTask throttleTask)
    {
        try {
            await throttleTask.Vhc();
        }
        catch (OperationCanceledException) {
            // disposed: the queue drops the packets it still holds
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not wait on the speed limit.");
        }
    }

    protected override void DisposeManaged()
    {
        // the queue first, so its loop takes no packet for a closing channel, then the wait it may be in
        _throttleQueue.Dispose();
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

        base.DisposeManaged();
    }

    // a turn of the queue routes its packet and pays the limit
    private sealed class ThrottleQueue(ThrottledTunnel tunnel, int capacity) : FairPacketQueue(capacity)
    {
        protected override ValueTask SendQueuedPacketAsync(IpPacket ipPacket)
        {
            return tunnel.SendThrottledPacketAsync(ipPacket);
        }
    }
}
