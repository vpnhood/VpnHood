using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfile
{
    public required Guid ClientProfileId { get; init; }
    public required string? ClientProfileName { get; set; }
    public required Token Token { get; set; }
    public bool IsFavorite { get; set; }
    public string? CustomData { get; set; }
    public bool IsPremiumLocationSelected { get; set; }
    public string? SelectedLocation { get; set; }
    public bool IsBuiltIn { get; set; }
    /// <summary>
    /// Holding a code IS the premium credential — refused, expired or spent (keyring plan §8).
    /// Nothing may flip this behind the person's back: every LOCAL premium gate reads it, so going
    /// false silently turns the build into its free edition — premium locations disappear, the
    /// promotion banner returns — and that is the one thing the app may not decide on its own.
    /// <para>
    /// The claim costs nothing to leave standing, because every premium-by-code feature is gated
    /// again by the access server at connect time. A dead code opens the local toggles and then
    /// fails the connection, which is where the person is told and offered Restore Premium or a new
    /// code. The server is the gate; this is only what the UI claims until the server answers.
    /// </para>
    /// </summary>
    public bool IsPremium => !Token.IsPublic || AccessCode != null;
    public string? AccessCode { get; set; }

    /// <summary>
    /// False while somebody has typed a code HERE that no account has taken. Two ordinary ways in:
    /// nobody is signed in, so there is no account to take it; or somebody is, but the portal was
    /// unreachable — which is routine where VpnHood is used, the portal often being blocked until a
    /// connection is up. The upload is therefore retried at the next account refresh and right after
    /// a successful connection (keyring plan §6).
    /// <para>
    /// Signing out is not one of those ways: it clears the code, and a cleared code owes nothing.
    /// Signing IN with the flag still false is how a code typed offline reaches the account — which
    /// is why the pre-sign-in prompt is a correctness requirement and not merely a courtesy: the
    /// upload that follows is the person's answer to it.
    /// </para>
    /// <para>
    /// While it is false, a refresh must upload this code instead of overwriting it with the
    /// account's, and signing out keeps the code: one the account never took never became the
    /// account's. True for anything that arrived from the account, for a typed code once its upload
    /// succeeded, and whenever there is no code at all.
    /// </para>
    /// <para>
    /// This is the ONLY provenance the design keeps, and it carries no time. Order between two
    /// uploads is whichever reaches the portal last — there is no stamp, no clock comparison and no
    /// sync protocol. It defaults to true so a profile written before this property existed is never
    /// mistaken for one owing an upload.
    /// </para>
    /// </summary>
    public bool IsAccessCodeSynced { get; set; } = true;

    public AccessCodeRefusal? AccessCodeRefusal { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? CustomServerEndpoints { get; set; }

    public bool IsCustomServerEndpointsEnabled { get; set; } = true;
}