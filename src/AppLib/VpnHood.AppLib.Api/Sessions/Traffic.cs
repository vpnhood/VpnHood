namespace VpnHood.AppLib.Api.Sessions;

// Bytes out and bytes in. No Total and no operators: a contract carries counts, and what a UI adds
// up is the UI's arithmetic.

/// <summary>
/// Represents an amount each way: bytes, or a rate where a member says so.
/// </summary>
public readonly record struct Traffic
{
    /// <summary>Gets the amount sent.</summary>
    public long Sent { get; init; }

    /// <summary>Gets the amount received.</summary>
    public long Received { get; init; }
}
