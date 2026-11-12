using VpnHood.AppLib.Api.Sessions;

namespace VpnHood.AppLib.Api.VpnProfiles;

/// <summary>
/// Represents the access server's refusal of a VPN profile's premium code, kept with the profile until a connection
/// succeeds or the code changes.
/// </summary>
/// <remarks>
/// The code stays, as its issuer may extend it, and the profile still claims premium: the refusal lets the UI say
/// truly whether the code expired or was refused, and keeps a sign-in quiet about a code the server does not know. It
/// is saved with the profile, as only the access server can give it.
/// </remarks>
public class AccessCodeRefusal
{
    /// <summary>
    /// Gets the access server's reason: only <see cref="SessionErrorCode.AccessExpired"/> may be shown as an expired code,
    /// and any other is a refusal.
    /// </summary>
    public required SessionErrorCode ErrorCode { get; init; }

    /// <summary>Gets when the access server refused the code, in UTC.</summary>
    public required DateTime RefusedTime { get; init; }
}
