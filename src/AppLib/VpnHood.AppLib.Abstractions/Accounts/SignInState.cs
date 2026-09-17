using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// What a SignIn call accomplished. Anything but <see cref="SignedIn"/> means NOTHING is signed in
/// yet — the account uses a second factor, and the caller must repeat SignIn with
/// SignInOptions.TwoFactorCode. One member per second-factor kind this app can actually answer:
/// a kind the build does not know is refused by the provider rather than handed to a UI that could
/// only show the wrong prompt.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignInState>))]
public enum SignInState
{
    /// <summary>The session is established.</summary>
    SignedIn,

    /// <summary>
    /// A code from the authenticator app is due (the account's backup code is always accepted too).
    /// </summary>
    TotpRequired
}
