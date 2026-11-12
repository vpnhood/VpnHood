using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Accounts;

/// <summary>Specifies what a sign-in did.</summary>
/// <remarks>
/// Anything but <see cref="SignedIn"/> signed nobody in: the account has a second step, and the sign-in is repeated with
/// <see cref="SignInOptions.TwoFactorCode"/>. A second step has a member only where the app can answer it, and the
/// sign-in refuses one this build does not know. In JSON it is a name.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SignInState>))]
public enum SignInState
{
    /// <summary>The person is signed in.</summary>
    SignedIn,

    /// <summary>A code from the authenticator app is due; the account's backup code is taken too.</summary>
    TotpRequired
}
