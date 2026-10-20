using System.Diagnostics;
using System.Net;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Server;

// The server's side of a session's UDP. It sends through the listener, and to the address, of the newest datagram it
// has authenticated, so a forged datagram, or a copy of an older one, moves nothing
internal sealed class ServerUdpPacketChannel(ulong sessionId, byte[] sessionKey, TrafficMeter trafficMeter)
    : UdpPacketChannel(sessionId, sessionKey, isServer: true, firstSendSequenceNumber: GetClock(), trafficMeter)
{
    // the clock a session's count starts from, which only grows: the wall clock at the start, in 100 ns since the
    // Unix epoch, plus a stopwatch
    private static readonly long ClockStartTicks = DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks;
    private static readonly long ClockStartTimestamp = Stopwatch.GetTimestamp();

    // two listeners may bring the session's datagrams at once
    private readonly Lock _receiveLock = new();
    private ulong _newestSequenceNumber;
    private ulong _expiredBeforeSequenceNumber;
    private volatile UdpRoute? _route;

    public UdpRoute? Route => _route;

    // Opens the datagram in place and says how fresh it is: the newest yet moves the route, and gives its seq for the
    // session's switch. A forgery throws before anything changes. Its packets go up with Deliver, unless it was seen,
    // once the session has acted on the result
    public DatagramFreshness Receive(UdpChannelTransmitter listener, Memory<byte> datagram, IPEndPoint remoteEndPoint,
        out ulong sequenceNumber)
    {
        lock (_receiveLock) {
            if (Open(datagram.Span) is not { } seq) {
                sequenceNumber = 0;
                return DatagramFreshness.Seen;
            }

            sequenceNumber = seq;
            if (seq <= _newestSequenceNumber)
                return DatagramFreshness.Older;

            _newestSequenceNumber = seq;
            FollowDatagram(listener, remoteEndPoint);
            return DatagramFreshness.Newest;
        }
    }

    // the opened datagram's packets, up to the tunnel; an older one's too, as a reordered datagram's would
    public void Deliver(Memory<byte> datagram)
    {
        HandUp(datagram[UdpDatagram.HeaderLength..]);
    }

    // A TCP packet channel request carries the seq of the client's next UDP datagram: those it sent before turning to
    // TCP are older, so a late one cannot turn the session back to UDP
    public void ExpireBefore(ulong sequenceNumber)
    {
        if (sequenceNumber == 0)
            return;

        lock (_receiveLock) {
            _expiredBeforeSequenceNumber = Math.Max(_expiredBeforeSequenceNumber, sequenceNumber);
            _newestSequenceNumber = Math.Max(_newestSequenceNumber, sequenceNumber - 1);
        }
    }

    // whether a TCP request has expired the datagram since it was opened as the newest
    public bool IsExpired(ulong sequenceNumber)
    {
        lock (_receiveLock)
            return sequenceNumber < _expiredBeforeSequenceNumber;
    }

    protected override UdpRoute? GetRoute() => _route;

    // a new route only when the newest datagram comes from somewhere else, so none is made per datagram
    private void FollowDatagram(UdpChannelTransmitter listener, IPEndPoint remoteEndPoint)
    {
        var route = _route;
        if (route is null || route.Transmitter != listener || !route.EndPoint.Equals(remoteEndPoint))
            _route = new UdpRoute(listener, remoteEndPoint);
    }

    // in nanoseconds: a session recovered after a restart keeps its key, and its new channel counts on above every
    // seq the old one sent, as long as that sent less than one datagram a nanosecond and the clock did not go back
    private static ulong GetClock()
    {
        return (ulong)(ClockStartTicks + Stopwatch.GetElapsedTime(ClockStartTimestamp).Ticks) * 100;
    }
}
