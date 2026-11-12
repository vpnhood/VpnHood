namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>Represents a sign-in: its method, with the credentials or the second step's code.</summary>
public record SignInOptions
{
    /// <summary>
    /// The sign-in method — one of <see cref="IAuthenticationProvider.ProviderIds"/> (see <see cref="AuthProviders"/>).
    /// </summary>
    public required string ProviderId { get; init; }
    /// <summary>
    /// Gets the email address, for the password sign-in's first step, or <see langword="null"/>.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>Gets the password, for the password sign-in's first step, or <see langword="null"/>.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// The second-step answer when the previous SignIn returned a state other than SignedIn: the
    /// authenticator code or the account's backup code. Sent alone (no UserName/Password) with the
    /// same ProviderId — the provider holds the pending challenge.
    /// </summary>
    public string? TwoFactorCode { get; init; }
}
