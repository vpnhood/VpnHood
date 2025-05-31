using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Tunneling;

public static class GeneralEventId
{
    public static EventId Session = new((int)EventCode.Session, nameof(Session));
    public static EventId Essential = new((int)EventCode.Essential, nameof(Essential));
    public static EventId Sni = new((int)EventCode.Sni, nameof(Sni));
    public static EventId Nat = new((int)EventCode.Nat, nameof(Nat));
    public static EventId Ping = new((int)EventCode.Ping, nameof(Ping));
    public static EventId Dns = new((int)EventCode.Dns, nameof(Dns));
    public static EventId Tcp = new((int)EventCode.Tcp, nameof(Tcp));
    public static EventId Tls = new((int)EventCode.Tls, nameof(Tls));
    public static EventId Udp = new((int)EventCode.Udp, nameof(Udp));
    public static EventId Packet = new((int)EventCode.Packet, nameof(Packet));
    public static EventId ProxyChannel = new((int)EventCode.StreamChannel, nameof(ProxyChannel));
    public static EventId PacketChannel = new((int)EventCode.PacketChannel, nameof(EventCode.PacketChannel));
    public static EventId Request = new((int)EventCode.Request, nameof(Request));
    public static EventId TcpLife = new((int)EventCode.TcpLife, nameof(TcpLife));
    public static EventId Test = new((int)EventCode.Test, nameof(Test));
    public static EventId UdpSign = new((int)EventCode.UdpSign, nameof(UdpSign));

    // server
    public static EventId AccessManager = new((int)EventCode.AccessManager, nameof(AccessManager));
    public static EventId NetProtect = new((int)EventCode.NetProtect, nameof(NetProtect));
    public static EventId NetFilter = new((int)EventCode.NetFilter, nameof(NetFilter));
    public static EventId DnsChallenge = new((int)EventCode.DnsChallenge, nameof(DnsChallenge));
    public static EventId SessionTrack = new((int)EventCode.SessionTrack, nameof(SessionTrack));
    public static EventId Track = new((int)EventCode.Track, nameof(Track));

    private enum EventCode
    {
        Essential = 10,
        Session,
        Sni,
        Nat,
        Ping,
        Dns,
        Packet,
        Tcp,
        Udp,
        UdpSign,
        StreamChannel,
        PacketChannel,
        Track,
        Tls,
        AccessManager,
        NetProtect,
        NetFilter,
        SessionTrack,
        Request,
        TcpLife,
        DnsChallenge,
        Test
    }
}