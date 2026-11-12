namespace VpnHood.AppLib.Api.Accounts;

/// <summary>Represents what a sign-in did.</summary>
/// <remarks>
/// With a <see cref="State"/> other than <see cref="SignInState.SignedIn"/>, nobody is signed in yet, and the sign-in is
/// repeated with <see cref="SignInOptions.TwoFactorCode"/>.
/// </remarks>
public record SignInResult
{
    /// <summary>Gets where the sign-in stands.</summary>
    public required SignInState State { get; init; }

    /// <summary>
    /// Gets the account's new backup code, after a sign-in that used the old one up, or <see langword="null"/>.
    /// </summary>
    /// <remarks>The UI shows it once: nothing shows it again.</remarks>
    public string? NewBackupCode { get; init; }
}
