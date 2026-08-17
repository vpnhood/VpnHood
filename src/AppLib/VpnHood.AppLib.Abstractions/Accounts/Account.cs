namespace VpnHood.AppLib.Abstractions.Accounts;

public class Account
{
    /// <summary>
    /// The backend's own identifier for this person — the account's identity, and the only member
    /// guaranteed to be here. Email cannot serve that role: it is mutable, and an account may be
    /// held through an identity that carries no address at all.
    /// </summary>
    public required string UserId { get; set; }

    public string? Name { get; set; }
    public string? Email { get; set; }

    /// <summary>The store subscription serving this account, or null when none does.</summary>
    public Subscription? Subscription { get; set; }

    // THE one access code serving this account, or null — whichever channel delivered it. The backend
    // chooses and ranks it, recomputing the choice on every read (lifecycle §8: the app is told a
    // code, not a list). Subscription says how forcefully to apply it: backed by one, it outranks even
    // a code the person typed, because they are paying for it right now; otherwise it only fills a
    // device that has no code at all. Either way it is ACCOUNT-granted and leaves with the account —
    // only a typed code is the person's own to keep.
    public AccessCodeInfo? AccessCodeInfo { get; set; }
}
