namespace VpnHood.Core.Packets.VhPackets;

public enum IcmpV4Code : byte
{
    // Type 3 - Destination Unreachable
    NetUnreachable = 0,
    HostUnreachable = 1,
    ProtocolUnreachable = 2,
    PortUnreachable = 3,
    FragmentationNeeded = 4,
    SourceRouteFailed = 5,
    DestinationNetworkUnknown = 6,
    DestinationHostUnknown = 7,
    SourceHostIsolated = 8,
    NetworkAdminProhibited = 9,
    HostAdminProhibited = 10,
    NetworkUnreachableForTos = 11,
    HostUnreachableForTos = 12,
    CommunicationAdminProhibited = 13,
    HostPrecedenceViolation = 14,
    PrecedenceCutoffInEffect = 15,

    // Type 5 - Redirect
    RedirectDatagramForNetwork = 0,
    RedirectDatagramForHost = 1,
    RedirectForTosAndNetwork = 2,
    RedirectForTosAndHost = 3,

    // Type 11 - Time Exceeded
    TimeToLiveExceededInTransit = 0,
    FragmentReassemblyTimeExceeded = 1,

    // Type 12 - Parameter Problem
    PointerIndicatesError = 0,
    MissingRequiredOption = 1,
    BadLength = 2
}