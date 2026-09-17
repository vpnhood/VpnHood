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

    /// <summary>
    /// THE one code this account serves, or null when it serves none. The backend ranks everything
    /// the account holds — whatever is being paid for right now first, then the best of the rest —
    /// and recomputes the winner on every read, so the app is handed a code and never a list, and
    /// never picks (keyring plan §2). Its expiry is what some device last reported and is advisory;
    /// only an access-server refusal ends use of a credential.
    /// </summary>
    public AccessCodeInfo? AccessCodeInfo { get; set; }

}
