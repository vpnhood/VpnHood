using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Tunneling;

/// <summary>Provides the event IDs of VpnHood's log lines, by what a line is about.</summary>
public static class GeneralEventId
{
    /// <summary>The event ID of the lines about a session.</summary>
    public static readonly EventId Session = new((int)EventCode.Session, nameof(Session));

    /// <summary>The event ID of the lines about a client's requests.</summary>
    public static readonly EventId Request = new((int)EventCode.Request, nameof(Request));

    internal static readonly EventId Essential = new((int)EventCode.Essential, nameof(Essential));

    /// <summary>The event ID of the lines about the channels that forward TCP connections.</summary>
    public static readonly EventId StreamForwardingChannel = new((int)EventCode.StreamChannel, nameof(StreamForwardingChannel));

    /// <summary>The event ID of the lines about the packet channels.</summary>
    public static readonly EventId PacketChannel = new((int)EventCode.PacketChannel, nameof(EventCode.PacketChannel));

    /// <summary>The event ID of the lines about streams and their connections.</summary>
    public static readonly EventId Stream = new((int)EventCode.Stream, nameof(Stream));

    /// <summary>The event ID of the lines tests write.</summary>
    public static readonly EventId Test = new((int)EventCode.Test, nameof(Test));

    /// <summary>The event ID of the lines about the UDP channel's datagrams that fail their check.</summary>
    public static readonly EventId UdpSign = new((int)EventCode.UdpSign, nameof(UdpSign));

    internal static readonly EventId Packet = new((int)EventCode.Packet, nameof(Packet));

    /// <summary>The event ID of the lines about the server names of TLS connections.</summary>
    public static readonly EventId Sni = new((int)EventCode.Sni, nameof(Sni));

    internal static readonly EventId Ping = new((int)EventCode.Ping, nameof(Ping));
    internal static readonly EventId Dns = new((int)EventCode.Dns, nameof(Dns));
    internal static readonly EventId Tcp = new((int)EventCode.Tcp, nameof(Tcp));
    internal static readonly EventId Udp = new((int)EventCode.Udp, nameof(Udp));

    /// <summary>The event ID of the lines about the server's access manager.</summary>
    public static readonly EventId AccessManager = new((int)EventCode.AccessManager, nameof(AccessManager));

    /// <summary>The event ID of the lines about the server's protection of its network.</summary>
    public static readonly EventId NetProtect = new((int)EventCode.NetProtect, nameof(NetProtect));

    internal static readonly EventId NetFilter = new((int)EventCode.NetFilter, nameof(NetFilter));

    /// <summary>The event ID of the lines about the server's ACME challenges, for its certificate.</summary>
    public static readonly EventId AcmeChallenge = new((int)EventCode.AcmeChallenge, nameof(AcmeChallenge));

    /// <summary>The event ID of the session log's lines: a server's record of each session's start and end.</summary>
    public static readonly EventId SessionTrack = new((int)EventCode.SessionTrack, nameof(SessionTrack));

    /// <summary>The event ID of the track log's lines: a server's record of its sessions' connections.</summary>
    public static readonly EventId Track = new((int)EventCode.Track, nameof(Track));

    private enum EventCode
    {
        Essential = 10,
        Session,
        Sni,
        Ping,
        Dns,
        Packet,
        Tcp,
        Udp,
        UdpSign,
        StreamChannel,
        PacketChannel,
        Track,
        AccessManager,
        NetProtect,
        NetFilter,
        SessionTrack,
        Request,
        Stream,
        AcmeChallenge,
        Test
    }
}