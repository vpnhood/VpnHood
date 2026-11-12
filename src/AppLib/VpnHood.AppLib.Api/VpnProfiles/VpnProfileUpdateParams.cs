using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.Api.VpnProfiles;

/// <summary>
/// Represents a change to a VPN profile: each part set changes, and each left <see langword="null"/> stays, but for
/// what removing the premium code clears (<see cref="AccessCode"/>).
/// </summary>
public class VpnProfileUpdateParams
{
    /// <summary>Gets or sets the new name, or <see langword="null"/> to keep it.</summary>
    public Patch<string?>? VpnProfileName { get; set; }

    /// <summary>Gets or sets whether the VPN profile is a favorite, or <see langword="null"/> to keep it.</summary>
    public Patch<bool>? IsFavorite { get; set; }

    /// <summary>Gets or sets the server location chosen, or <see langword="null"/> to keep it.</summary>
    public Patch<string?>? SelectedLocation { get; set; }

    /// <summary>Gets or sets the text a UI keeps with the VPN profile, or <see langword="null"/> to keep it.</summary>
    public Patch<string?>? CustomData { get; set; }

    /// <summary>Gets or sets whether a premium location is chosen, or <see langword="null"/> to keep it.</summary>
    public Patch<bool>? IsPremiumLocationSelected { get; set; }

    /// <summary>
    /// Gets or sets the premium code typed for the VPN profile, or <see langword="null"/> to keep the profile's; a
    /// signed-in account gets it uploaded.
    /// </summary>
    /// <remarks>
    /// A different code, or none, clears the profile's <see cref="VpnProfileInfo.AccessCodeRefusal"/>; where an existing
    /// code is removed, the premium location and the chosen location are cleared too, even where this change sets them.
    /// </remarks>
    public Patch<string?>? AccessCode { get; set; }

    /// <summary>Gets or sets the server's addresses the person gives, or <see langword="null"/> to keep them.</summary>
    public Patch<string[]?>? CustomServerEndpoints { get; set; }

    /// <summary>Gets or sets whether the VPN profile connects to its own server addresses, or <see langword="null"/> to keep it.</summary>
    public Patch<bool>? IsCustomServerEndpointsEnabled { get; set; }
}