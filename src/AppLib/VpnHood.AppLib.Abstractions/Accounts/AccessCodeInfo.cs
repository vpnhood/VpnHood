namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// THE one access code serving an account, with its own clock — whichever channel delivered it.
/// The backend chooses and ranks it, recomputing the choice on every read (lifecycle §8: the app
/// is told a code, not a list).
/// </summary>
public class AccessCodeInfo
{
    public required string AccessCode { get; set; }

    /// <summary>The code's own clock where the backend can read it; null for a one-time code that has not started.</summary>
    public DateTime? ExpirationTime { get; set; }
}
