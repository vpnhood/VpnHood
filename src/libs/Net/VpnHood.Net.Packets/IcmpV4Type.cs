namespace VpnHood.Net.Packets;

/// <summary>
/// Specifies the type of an ICMPv4 message (RFC 792 and its successors).
/// </summary>
public enum IcmpV4Type : byte
{
    /// <summary>An echo reply, the answer to a ping.</summary>
    EchoReply = 0,

    /// <summary>The datagram could not be delivered; the code says why.</summary>
    DestinationUnreachable = 3,

    /// <summary>A request to slow down (deprecated by RFC 6633).</summary>
    SourceQuench = 4,

    /// <summary>A better route exists; the code says for what.</summary>
    Redirect = 5,

    /// <summary>An echo request, a ping.</summary>
    EchoRequest = 8,

    /// <summary>The datagram's time to live, or its reassembly time, ran out.</summary>
    TimeExceeded = 11,

    /// <summary>A header field of the datagram is wrong.</summary>
    ParameterProblem = 12,

    /// <summary>A timestamp request.</summary>
    TimestampRequest = 13,

    /// <summary>A timestamp reply.</summary>
    TimestampReply = 14,

    /// <summary>An information request (obsolete).</summary>
    InfoRequest = 15,

    /// <summary>An information reply (obsolete).</summary>
    InfoReply = 16,

    /// <summary>An address mask request (deprecated by RFC 6918).</summary>
    AddressMaskRequest = 17,

    /// <summary>An address mask reply (deprecated by RFC 6918).</summary>
    AddressMaskReply = 18
}
