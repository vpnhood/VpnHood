namespace VpnHood.Core.Server;

// how a session's datagram stands against those it received before
internal enum DatagramFreshness
{
    // a copy of one received before, or older than the replay window: it goes no further
    Seen,

    // received for the first time after a newer one, as a reordered datagram is: its packets go up
    Older,

    // the newest yet: it moves the route, and may turn UDP on
    Newest
}
