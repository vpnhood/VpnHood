namespace VpnHood.AppLib.Api.Accounts;

/// <summary>Represents the signed-in person's account, as the account backend gives it.</summary>
public class Account
{
    /// <summary>
    /// Gets or sets the account backend's ID of the person: the account's identity, and the one member always set.
    /// </summary>
    /// <remarks>The email address cannot serve as one: it can change, and an identity may carry none.</remarks>
    public required string UserId { get; set; }

    /// <summary>Gets or sets the person's name, or <see langword="null"/>.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the person's email address, or <see langword="null"/> where the account's identity carries none.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the store subscription that serves the account, or <see langword="null"/> where none does.</summary>
    public Subscription? Subscription { get; set; }

    /// <summary>
    /// Gets or sets the premium code the account serves, or <see langword="null"/> where it serves none.
    /// </summary>
    /// <remarks>
    /// The account backend picks it at each read from what the account holds, what is paid for now first. Its expiry
    /// informs the UI; only the access server's refusal ends the code's use.
    /// </remarks>
    public AccessCodeInfo? AccessCodeInfo { get; set; }

}
