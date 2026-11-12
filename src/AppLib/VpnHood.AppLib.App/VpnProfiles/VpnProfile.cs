using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.App.VpnProfiles;

/// <summary>
/// Represents a VPN profile as the storage keeps it: an access key the app connects with, and what the person chose for
/// it.
/// </summary>
public class VpnProfile
{
    /// <summary>Gets the profile's ID.</summary>
    public required Guid VpnProfileId { get; init; }

    /// <summary>Gets or sets the profile's name, the key's own at first, or <see langword="null"/>.</summary>
    public required string? VpnProfileName { get; set; }

    /// <summary>Gets or sets the access key's token, which the app connects with.</summary>
    public required Token Token { get; set; }

    /// <summary>Gets or sets a value indicating whether the profile is a favorite.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Gets or sets the text a UI keeps with the profile, which the app does not read, or <see langword="null"/>.
    /// </summary>
    public string? CustomData { get; set; }

    /// <summary>Gets or sets a value indicating whether the location chosen is a premium one.</summary>
    public bool IsPremiumLocationSelected { get; set; }

    /// <summary>Gets or sets the server location chosen, or <see langword="null"/> for none.</summary>
    public string? SelectedLocation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the profile is one of the app's built-in access keys
    /// (<see cref="AppOptions.AccessKeys"/>), which an access key the person imports may not overwrite.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Gets a value indicating whether the profile is premium: its key is not public, or it holds a premium code, even
    /// one refused, expired or spent.
    /// </summary>
    /// <remarks>
    /// The app's own premium gates read it, such as its premium locations and its promotion, so nothing changes it
    /// behind the person's back. The access server checks the code again at each connect; where it refuses it, the app
    /// may get another code from the account and try again before it reports the refusal.
    /// </remarks>
    public bool IsPremium => !Token.IsPublic || AccessCode != null;

    /// <summary>Gets or sets the premium code the profile connects with, or <see langword="null"/> for none.</summary>
    public string? AccessCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the account has the profile's premium code: <see langword="false"/>
    /// while a code typed on this device waits for an account to take it, as when nobody is signed in or the account
    /// backend could not be reached. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// When someone is signed in, the app tries to upload the selected profile's waiting code at the next account
    /// refresh and right after a connect; a sign-out keeps a waiting code, as a code the account never took is the
    /// device's own. It is <see langword="true"/> for a code from the account, for a typed code once uploaded, and
    /// where there is no code.
    /// </remarks>
    public bool IsAccessCodeSynced { get; set; } = true;

    /// <summary>Gets or sets the server's refusal of the premium code, or <see langword="null"/> for none.</summary>
    public AccessCodeRefusal? AccessCodeRefusal { get; set; }

    /// <summary>Gets or sets the server's addresses the person gave, or <see langword="null"/> for none.</summary>
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? CustomServerEndpoints { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the profile connects to its own server addresses
    /// (<see cref="CustomServerEndpoints"/>). The default is <see langword="true"/>.
    /// </summary>
    public bool IsCustomServerEndpointsEnabled { get; set; } = true;
}