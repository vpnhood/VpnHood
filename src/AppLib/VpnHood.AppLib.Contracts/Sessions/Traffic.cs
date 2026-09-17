namespace VpnHood.AppLib.Contracts.Sessions;

// Bytes out and bytes in. No Total and no operators: a contract carries counts, and what a UI adds
// up is the UI's arithmetic.
public readonly record struct Traffic
{
    public long Sent { get; init; }
    public long Received { get; init; }
}
