namespace VpnHood.Core.Packets;

public enum IcmpV6Code : byte
{
    // For Destination Unreachable (Type 1)
    NoRoute = 0,
    AdminProhibited = 1,
    BeyondScope = 2,
    AddressUnreachable = 3,
    PortUnreachable = 4,
    SourceAddressFailedPolicy = 5,
    RejectRoute = 6,
    ErrorInSourceRoutingHeader = 7,
    HeadersTooLong = 8,
    PRouteError = 9,

    // For Type 2: Packet Too Big
    PacketTooBig = 0,

    // For Time Exceeded (Type 3)
    HopLimitExceeded = 0,
    FragmentReassemblyTimeExceeded = 1,

    // For Parameter Problem (Type 4)
    ErroneousHeaderField = 0,
    UnknownNextHeader = 1,
    UnrecognizedOption = 2,
    IncompleteHeaderChain = 3,
    UpperLayerHeaderError = 4,
    UnknownNextHeaderByIntermediate = 5,
    ExtensionHeaderTooBig = 6,
    HeaderChainTooLong = 7,
    TooManyExtensionHeaders = 8,
    TooManyOptions = 9,
    OptionTooBig = 10
}