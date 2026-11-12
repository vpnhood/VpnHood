namespace VpnHood.AppLib.Api.Accounts;

/// <summary>Represents a sign-in: its method, with the credentials or the second step's code.</summary>
public record SignInOptions
{
    /// <summary>
    /// Gets the sign-in method, one of <see cref="VpnHood.AppLib.Api.App.AppDescriptor.AuthProviderIds"/>
    /// (<see cref="AuthProviders"/>).
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>Gets the email address, for the password sign-in's first step, or <see langword="null"/>.</summary>
    public string? UserName { get; init; }

    /// <summary>Gets the password, for the password sign-in's first step, or <see langword="null"/>.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the second step's code, the authenticator app's or the account's backup code, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// It goes alone, without <see cref="UserName"/> and <see cref="Password"/>, with the same <see cref="ProviderId"/>,
    /// after a sign-in whose state was not <see cref="SignInState.SignedIn"/>.
    /// </remarks>
    public string? TwoFactorCode { get; init; }
}
