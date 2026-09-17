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
    /// The code's own clock where the backend can read it, else null. ADVISORY DISPLAY ONLY: it
    /// never decides anything here, and nothing reports one back. Whether a code still works is the
    /// access server's verdict at connect time, and the account's own answer to that is
    /// <i>eligible or rejected</i> — one bit, with no dates in it (keyring plan §4).
    /// </summary>
    public DateTime? ExpirationTime { get; set; }
}
