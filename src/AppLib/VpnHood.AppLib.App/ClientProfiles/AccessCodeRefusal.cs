using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.ClientProfiles;

/// <summary>
/// The access server refused this profile's access code (keyring plan §8). The code is KEPT —
/// refusal never deletes a credential; its issuer may extend it, and a later successful connection
/// clears this mark by itself.
/// <para>
/// The mark does NOT stop the profile claiming premium. Letting it do so turned the build into its
/// own free edition without anyone deciding to — premium locations gone, promotion banner back —
/// which is the one thing the app may not do on its own. What the mark is for is saying
/// <i>expired</i> rather than <i>rejected</i> truthfully, and staying quiet at the next sign-in
/// about a code the server has never heard of.
/// </para>
/// Persisted with the profile, because only the access server can give this answer and a restart
/// must not forget it.
/// </summary>
public class AccessCodeRefusal
{
    /// <summary>Only AccessExpired may be described as "expired"; anything else is a rejection.</summary>
    public required SessionErrorCode ErrorCode { get; init; }

    public required DateTime RefusedTime { get; init; }
}
