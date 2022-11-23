using Microsoft.Extensions.Logging;

namespace VpnHood.Tunneling;

public static class GeneralEventId
{
    public static EventId Session = new((int)EventCode.Session, nameof(Session));
    public static EventId Nat = new((int)EventCode.Nat, nameof(Nat));
    public static EventId Ping = new((int)EventCode.Ping, nameof(Ping));
    public static EventId Dns = new((int)EventCode.Dns, nameof(Dns));
    public static EventId Tcp = new((int)EventCode.Tcp, nameof(Tcp));
    public static EventId Tls = new((int)EventCode.Tls, nameof(Tls));
    public static EventId Udp = new((int)EventCode.Udp, nameof(Udp));
    public static EventId Track = new((int)EventCode.Track, nameof(Track));
    public static EventId StreamChannel = new((int)EventCode.StreamChannel, nameof(StreamChannel));
    public static EventId DatagramChannel = new((int)EventCode.DatagramChannel, EventCode.DatagramChannel.ToString());

    private enum EventCode
    {
        Session = 10,
        Nat,
        Ping,
        Dns,
        Tcp,
        Udp,
        StreamChannel,
        DatagramChannel,
        Track,
        Tls
    }
}