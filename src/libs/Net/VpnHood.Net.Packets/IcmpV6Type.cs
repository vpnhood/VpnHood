namespace VpnHood.Net.Packets;

/// <summary>
/// Specifies the type of an ICMPv6 message, as IANA registers it. Types below 128 are errors; the others are
/// informational.
/// </summary>
// This enumeration was originally derived from PacketDotNet.
// ReSharper disable IdentifierTypo
public enum IcmpV6Type : byte
{
    /// <summary>The packet could not be delivered; the code says why (RFC 4443).</summary>
    DestinationUnreachable = 1,

    /// <summary>The packet is larger than the next link's MTU, which the message carries (RFC 4443).</summary>
    PacketTooBig = 2,

    /// <summary>The hop limit, or the fragment reassembly time, ran out (RFC 4443).</summary>
    TimeExceeded = 3,

    /// <summary>A field of the packet is wrong; the code says which kind (RFC 4443).</summary>
    ParameterProblem = 4,

    /// <summary>An error type for private experimentation (RFC 4443).</summary>
    PrivateExperimentation1 = 100,

    /// <summary>An error type for private experimentation (RFC 4443).</summary>
    PrivateExperimentation2 = 101,

    /// <summary>Reserved for expanding the error types (RFC 4443).</summary>
    ReservedForExpansion1 = 127,

    /// <summary>An echo request, a ping (RFC 4443).</summary>
    EchoRequest = 128,

    /// <summary>An echo reply, the answer to a ping (RFC 4443).</summary>
    EchoReply = 129,

    /// <summary>A multicast listener query (RFC 2710).</summary>
    MulticastListenerQuery = 130,

    /// <summary>A multicast listener report (RFC 2710).</summary>
    MulticastListenerReport = 131,

    /// <summary>A multicast listener done message (RFC 2710).</summary>
    MulticastListenerDone = 132,

    /// <summary>A router solicitation (RFC 4861).</summary>
    RouterSolicitation = 133,

    /// <summary>A router advertisement (RFC 4861).</summary>
    RouterAdvertisement = 134,

    /// <summary>A neighbor solicitation (RFC 4861).</summary>
    NeighborSolicitation = 135,

    /// <summary>A neighbor advertisement (RFC 4861).</summary>
    NeighborAdvertisement = 136,

    /// <summary>A redirect message (RFC 4861).</summary>
    RedirectMessage = 137,

    /// <summary>A router renumbering message (RFC 2894).</summary>
    RouterRenumbering = 138,

    /// <summary>An ICMP node information query (RFC 4620).</summary>
    IcmpNodeInformationQuery = 139,

    /// <summary>An ICMP node information response (RFC 4620).</summary>
    IcmpNodeInformationResponse = 140,

    /// <summary>An inverse neighbor discovery solicitation (RFC 3122).</summary>
    InverseNeighborDiscoverySolicitationMessage = 141,

    /// <summary>An inverse neighbor discovery advertisement (RFC 3122).</summary>
    InverseNeighborDiscoveryAdvertisementMessage = 142,

    /// <summary>A version 2 multicast listener report (RFC 3810).</summary>
    Version2MulticastListenerReport = 143,

    /// <summary>A home agent address discovery request (RFC 6275).</summary>
    HomeAgentAddressDiscoveryRequestMessage = 144,

    /// <summary>A home agent address discovery reply (RFC 6275).</summary>
    HomeAgentAddressDiscoveryReplyMessage = 145,

    /// <summary>A mobile prefix solicitation (RFC 6275).</summary>
    MobilePrefixSolicitation = 146,

    /// <summary>A mobile prefix advertisement (RFC 6275).</summary>
    MobilePrefixAdvertisement = 147,

    /// <summary>A certification path solicitation (RFC 3971).</summary>
    CertificationPathSolicitationMessage = 148,

    /// <summary>A certification path advertisement (RFC 3971).</summary>
    CertificationPathAdvertisementMessage = 149,

    /// <summary>A message for experimental mobility protocols (RFC 4065).</summary>
    ExperimentalMobilityProtocols = 150,

    /// <summary>A multicast router advertisement (RFC 4286).</summary>
    MulticastRouterAdvertisement = 151,

    /// <summary>A multicast router solicitation (RFC 4286).</summary>
    MulticastRouterSolicitation = 152,

    /// <summary>A multicast router termination (RFC 4286).</summary>
    MulticastRouterTermination = 153,

    /// <summary>A fast handovers for Mobile IPv6 message (RFC 5568).</summary>
    FmIPv6Messages = 154,

    /// <summary>An RPL control message (RFC 6550).</summary>
    RplControlMessage = 155,

    /// <summary>An ILNPv6 locator update message (RFC 6743).</summary>
    IlnpV6LocatorUpdateMessage = 156,

    /// <summary>A duplicate address request (RFC 6775).</summary>
    DuplicateAddressRequest = 157,

    /// <summary>A duplicate address confirmation (RFC 6775).</summary>
    DuplicateAddressConfirmation = 158,

    /// <summary>An MPL control message (RFC 7731).</summary>
    MplControlMessage = 159,

    /// <summary>An extended echo request (RFC 8335).</summary>
    ExtendedEchoRequest = 160,

    /// <summary>An extended echo reply (RFC 8335).</summary>
    ExtendedEchoReply = 161,

    /// <summary>An informational type for private experimentation (RFC 4443).</summary>
    PrivateExperimentation3 = 200,

    /// <summary>An informational type for private experimentation (RFC 4443).</summary>
    PrivateExperimentation4 = 201,

    /// <summary>Reserved for expanding the informational types (RFC 4443).</summary>
    ReservedForExpansion2 = 255
}
