namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// THE one access code serving an account, whichever channel delivered it. The backend ranks
/// everything the account holds — what is being paid for right now first, then the best of the rest —
/// and recomputes the winner on every read, so the app is told a code and never a list
/// (keyring plan §2). Nothing is stored as <i>the</i> selection, so nothing needs repairing when a
/// code dies.
/// </summary>
public class AccessCodeInfo
{
    public required string AccessCode { get; set; }

    /// <summary>
    /// What this account last learned about the code's clock, or null when nobody has connected with
    /// it yet — which is also why an unknown expiry ranks first: trying the code is how the expiry
    /// gets learned.
    /// <para>
    /// The backend does not discover this. A device reports the expiry it saw after a connection or
    /// an authoritative refusal, and the value is kept PER ACCOUNT: the same bearer code may live in
    /// other accounts, and one account's stale report must never blank the code for someone else who
    /// is using it perfectly well (keyring plan §4).
    /// </para>
    /// </summary>
    public DateTime? ExpirationTime { get; set; }
}
