namespace VpnHood.AppLib.Abstractions.Accounts;

public record SignInOptions
{
    /// <summary>The identity provider to sign in with — one of AppFeatures.AuthProviderIds (see AuthProviders).</summary>
    public required string ProviderId { get; init; }
    public string? UserName { get; init; }
    public string? Password { get; init; }

    /// <summary>
    /// The second-step answer when the previous SignIn returned a state other than SignedIn: the
    /// authenticator code or the account's backup code. Sent alone (no UserName/Password) with the
    /// same ProviderId — the provider holds the pending challenge.
    /// </summary>
    public string? TwoFactorCode { get; init; }
}
