namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// What a SignIn call accomplished. See <see cref="SignInState"/>: anything but SignedIn means
/// nothing is signed in yet and the caller must repeat SignIn with SignInOptions.TwoFactorCode.
/// </summary>
public record SignInResult
{
    public required SignInState State { get; init; }

    /// <summary>
    /// Only with <see cref="SignInState.SignedIn"/>, and only after a completion that spent the
    /// backup code: the replacement. The UI must show it once — nothing ever shows it again.
    /// </summary>
    public string? NewBackupCode { get; init; }
}
