namespace VpnHood.AppLib.Portal.Dto;

/// <summary>
/// The password form's second-factor challenge (`challenge` in the 200 answer). The token is not a
/// session: single-use, minutes long, a small attempt budget, and it can do nothing but complete
/// its own challenge.
/// </summary>
public class PortalSignInChallenge
{
    /// <summary>The only kind this app can prompt for; the provider refuses anything else.</summary>
    public const string TypeTotp = "totp";

    public required string Token { get; init; }

    /// <summary>The second-factor kind ("totp" today). The backup code is accepted regardless of type.</summary>
    public required string Type { get; init; }

    public DateTime? ExpiresAt { get; init; }
}
