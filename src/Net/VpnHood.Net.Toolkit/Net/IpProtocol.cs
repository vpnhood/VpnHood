// ReSharper disable InconsistentNaming
namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Specifies what an IP packet carries: the IPv4 header's Protocol field, or the IPv6 header's Next Header field, by
/// the numbers IANA assigns.
/// </summary>
public enum IpProtocol : byte
{
    /// <summary>The IPv6 Hop-by-Hop Options header (0).</summary>
    IPv6HopByHopOptions = 0,

    /// <summary>The Internet Control Message Protocol for IPv4, ICMP (1).</summary>
    IcmpV4 = 1,

    /// <summary>The Internet Group Management Protocol, IGMP (2).</summary>
    Igmp = 2,

    /// <summary>The Gateway-to-Gateway Protocol, GGP (3).</summary>
    Gpg = 3,

    /// <summary>IPv4 encapsulated in IP (4).</summary>
    IPv4 = 4,

    /// <summary>The Transmission Control Protocol, TCP (6).</summary>
    Tcp = 6,

    /// <summary>The Exterior Gateway Protocol, EGP (8).</summary>
    Egp = 8,

    /// <summary>The PARC Universal Packet protocol, PUP (12).</summary>
    Pup = 12,

    /// <summary>The User Datagram Protocol, UDP (17).</summary>
    Udp = 17,

    /// <summary>The Xerox Network Systems Internet Datagram Protocol, XNS-IDP (22).</summary>
    Idp = 22,

    /// <summary>The ISO Transport Protocol Class 4, ISO-TP4 (29).</summary>
    Tp = 29,

    /// <summary>IPv6 encapsulated in IP (41).</summary>
    IPv6 = 41,

    /// <summary>The IPv6 Routing header (43).</summary>
    IPv6RoutingHeader = 43,

    /// <summary>The IPv6 Fragment header (44).</summary>
    IPv6FragmentHeader = 44,

    /// <summary>The Resource Reservation Protocol, RSVP (46).</summary>
    Rsvp = 46,

    /// <summary>Generic Routing Encapsulation, GRE (47).</summary>
    Gre = 47,

    /// <summary>The IPsec Encapsulating Security Payload, ESP (50).</summary>
    IpSecEncapsulatingSecurityPayload = 50,

    /// <summary>The IPsec Authentication Header, AH (51).</summary>
    IpSecAuthenticationHeader = 51,

    /// <summary>The Internet Control Message Protocol for IPv6, ICMPv6 (58).</summary>
    IcmpV6 = 58,

    /// <summary>No Next Header for IPv6: nothing follows the header (59).</summary>
    IPv6NoNextHeader = 59,

    /// <summary>The IPv6 Destination Options header (60).</summary>
    IPv6DestinationOptions = 60,

    /// <summary>Open Shortest Path First, OSPF (89).</summary>
    Ospf = 89,

    /// <summary>The Multicast Transport Protocol, MTP (92).</summary>
    Mtp = 92,

    /// <summary>The Encapsulation Header, ENCAP (98).</summary>
    Encapsulation = 98,

    /// <summary>Protocol Independent Multicast, PIM (103).</summary>
    Pim = 103,

    /// <summary>The IP Payload Compression Protocol, IPComp (108).</summary>
    CompressionHeader = 108,

    /// <summary>The Mobility Header of Mobile IPv6 (135).</summary>
    MobilityHeader = 135,

    /// <summary>The Host Identity Protocol, HIP (139).</summary>
    HostIdentity = 139,

    /// <summary>The Shim6 protocol (140).</summary>
    Shim6 = 140,

    /// <summary>Reserved for experiments and tests, RFC 3692 (253).</summary>
    Reserved253 = 253,

    /// <summary>Reserved for experiments and tests, RFC 3692 (254).</summary>
    Reserved254 = 254,

    /// <summary>
    /// The value 255, which IANA reserves. <see cref="Raw"/> has the same value, so 255 may print as either name.
    /// </summary>
    Mask = 255,

    /// <summary>
    /// The value 255, which a raw socket takes as its protocol. <see cref="Mask"/> has the same value, so 255 may print
    /// as either name.
    /// </summary>
    Raw = 255
}