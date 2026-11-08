namespace VpnHood.Net.Packets;

/// <summary>
/// Specifies the Explicit Congestion Notification (ECN) codepoint of an IP header (RFC 3168).
/// </summary>
public enum IpEcnField : byte
{
    /// <summary>Not ECN-capable transport (binary 00).</summary>
    NonEct = 0,

    /// <summary>ECN-capable transport, ECT(1) (binary 01).</summary>
    Ect1 = 1,

    /// <summary>ECN-capable transport, ECT(0) (binary 10).</summary>
    Ect0 = 2,

    /// <summary>Congestion experienced (binary 11).</summary>
    Ce = 3
}
