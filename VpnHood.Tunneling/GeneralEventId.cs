using Microsoft.Extensions.Logging;

namespace VpnHood.Tunneling;

public static class GeneralEventId
{
    public static EventId Session = new((int)EventCode.Session, nameof(Session));
    public static EventId SessionTrack = new((int)EventCode.SessionTrack, nameof(SessionTrack));
    public static EventId Nat = new((int)EventCode.Nat, nameof(Nat));
    public static EventId Ping = new((int)EventCode.Ping, nameof(Ping));
    public static EventId Dns = new((int)EventCode.Dns, nameof(Dns));
    public static EventId Tcp = new((int)EventCode.Tcp, nameof(Tcp));
    public static EventId Tls = new((int)EventCode.Tls, nameof(Tls));
    public static EventId Udp = new((int)EventCode.Udp, nameof(Udp));
    public static EventId Track = new((int)EventCode.Track, nameof(Track));
    public static EventId TcpProxyChannel = new((int)EventCode.StreamChannel, nameof(TcpProxyChannel));
    public static EventId DatagramChannel = new((int)EventCode.DatagramChannel, EventCode.DatagramChannel.ToString());
    public static EventId AccessServer = new((int)EventCode.AccessServer, nameof(AccessServer));
    public static EventId NetProtect = new((int)EventCode.NetProtect, nameof(NetProtect));

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
        Tls,
        AccessServer,
        NetProtect,
        SessionTrack
    }
}