namespace VpnHood.Net.Packets;

/// <summary>
/// Specifies the code of an ICMPv6 error message (RFC 4443 and its successors). A code means something only
/// with its type, so values repeat: each value names the type it belongs to.
/// </summary>
public enum IcmpV6Code : byte
{
    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: no route to the destination.</summary>
    NoRoute = 0,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: communication with the destination is administratively prohibited.</summary>
    AdminProhibited = 1,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: the destination is beyond the scope of the source address.</summary>
    BeyondScope = 2,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: the address is unreachable.</summary>
    AddressUnreachable = 3,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: the port is unreachable.</summary>
    PortUnreachable = 4,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: the source address failed an ingress or egress policy.</summary>
    SourceAddressFailedPolicy = 5,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: the route to the destination is rejected.</summary>
    RejectRoute = 6,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: the source routing header is in error (RFC 6554).</summary>
    ErrorInSourceRoutingHeader = 7,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: the headers are too long (RFC 8883).</summary>
    HeadersTooLong = 8,

    /// <summary><see cref="IcmpV6Type.DestinationUnreachable"/>: an error in a projected route (P-Route) of RPL.</summary>
    PRouteError = 9,

    /// <summary>The only code of <see cref="IcmpV6Type.PacketTooBig"/>.</summary>
    PacketTooBig = 0,

    /// <summary><see cref="IcmpV6Type.TimeExceeded"/>: the hop limit ran out in transit.</summary>
    HopLimitExceeded = 0,

    /// <summary><see cref="IcmpV6Type.TimeExceeded"/>: the fragment reassembly time ran out.</summary>
    FragmentReassemblyTimeExceeded = 1,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: an erroneous header field.</summary>
    ErroneousHeaderField = 0,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: an unrecognized Next Header type.</summary>
    UnknownNextHeader = 1,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: an unrecognized IPv6 option.</summary>
    UnrecognizedOption = 2,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: the first fragment has an incomplete header chain (RFC 7112).</summary>
    IncompleteHeaderChain = 3,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: an error in the upper-layer header of a segment routing packet (RFC 8754).</summary>
    UpperLayerHeaderError = 4,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: an unrecognized Next Header type met by an intermediate node (RFC 8883).</summary>
    UnknownNextHeaderByIntermediate = 5,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: an extension header is too big (RFC 8883).</summary>
    ExtensionHeaderTooBig = 6,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: the extension header chain is too long (RFC 8883).</summary>
    HeaderChainTooLong = 7,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: too many extension headers (RFC 8883).</summary>
    TooManyExtensionHeaders = 8,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: too many options in an extension header (RFC 8883).</summary>
    TooManyOptions = 9,

    /// <summary><see cref="IcmpV6Type.ParameterProblem"/>: an option is too big (RFC 8883).</summary>
    OptionTooBig = 10
}
