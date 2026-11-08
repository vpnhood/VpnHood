namespace VpnHood.Net.Packets;

/// <summary>
/// Specifies the code of an ICMPv4 message. A code means something only with its type, so values repeat:
/// each value names the type it belongs to.
/// </summary>
public enum IcmpV4Code : byte
{
    /// <summary>The only code of <see cref="IcmpV4Type.EchoReply"/> and <see cref="IcmpV4Type.EchoRequest"/>.</summary>
    EchoReply = 0,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the network is unreachable.</summary>
    NetUnreachable = 0,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the host is unreachable.</summary>
    HostUnreachable = 1,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the protocol is unreachable.</summary>
    ProtocolUnreachable = 2,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the port is unreachable.</summary>
    PortUnreachable = 3,

    /// <summary>
    /// <see cref="IcmpV4Type.DestinationUnreachable"/>: the datagram needs fragmenting but Don't Fragment is set;
    /// the next hop's MTU is in the low 16 bits of the message-specific field (RFC 1191).
    /// </summary>
    FragmentationNeeded = 4,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the source route failed.</summary>
    SourceRouteFailed = 5,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the destination network is unknown.</summary>
    DestinationNetworkUnknown = 6,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the destination host is unknown.</summary>
    DestinationHostUnknown = 7,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the source host is isolated.</summary>
    SourceHostIsolated = 8,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the network is administratively prohibited.</summary>
    NetworkAdminProhibited = 9,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the host is administratively prohibited.</summary>
    HostAdminProhibited = 10,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the network is unreachable for the type of service.</summary>
    NetworkUnreachableForTos = 11,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the host is unreachable for the type of service.</summary>
    HostUnreachableForTos = 12,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: communication is administratively prohibited.</summary>
    CommunicationAdminProhibited = 13,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: the host's precedence is violated.</summary>
    HostPrecedenceViolation = 14,

    /// <summary><see cref="IcmpV4Type.DestinationUnreachable"/>: a precedence cutoff is in effect.</summary>
    PrecedenceCutoffInEffect = 15,

    /// <summary><see cref="IcmpV4Type.Redirect"/>: redirect datagrams for the network.</summary>
    RedirectDatagramForNetwork = 0,

    /// <summary><see cref="IcmpV4Type.Redirect"/>: redirect datagrams for the host.</summary>
    RedirectDatagramForHost = 1,

    /// <summary><see cref="IcmpV4Type.Redirect"/>: redirect datagrams for the type of service and network.</summary>
    RedirectForTosAndNetwork = 2,

    /// <summary><see cref="IcmpV4Type.Redirect"/>: redirect datagrams for the type of service and host.</summary>
    RedirectForTosAndHost = 3,

    /// <summary><see cref="IcmpV4Type.TimeExceeded"/>: the time to live ran out in transit.</summary>
    TimeToLiveExceededInTransit = 0,

    /// <summary><see cref="IcmpV4Type.TimeExceeded"/>: the fragment reassembly time ran out.</summary>
    FragmentReassemblyTimeExceeded = 1,

    /// <summary><see cref="IcmpV4Type.ParameterProblem"/>: the pointer in the message points at the error.</summary>
    PointerIndicatesError = 0,

    /// <summary><see cref="IcmpV4Type.ParameterProblem"/>: a required option is missing.</summary>
    MissingRequiredOption = 1,

    /// <summary><see cref="IcmpV4Type.ParameterProblem"/>: the length is bad.</summary>
    BadLength = 2
}
