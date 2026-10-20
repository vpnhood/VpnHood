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
    private volatile UdpRoute? _route;

    public UdpRoute? Route => _route;

    // Opens the datagram in place: true for the newest yet, which moves the route. A forgery throws before anything
    // changes; a copy of an older datagram moves nothing. Its packets go up with Deliver, once the session has acted on
    // the result
    public bool Receive(UdpChannelTransmitter listener, Memory<byte> datagram, IPEndPoint remoteEndPoint)
    {
        lock (_receiveLock) {
            var seq = Open(datagram.Span);
            if (seq <= _newestSequenceNumber)
                return false;

            _newestSequenceNumber = seq;
            FollowDatagram(listener, remoteEndPoint);
            return true;
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

        lock (_receiveLock)
            _newestSequenceNumber = Math.Max(_newestSequenceNumber, sequenceNumber - 1);
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
