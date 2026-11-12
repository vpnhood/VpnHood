using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.VpnProfiles;

/// <summary>
/// Represents a VPN profile as the UI reads it, for the client's country: the policy and the locations depend on it.
/// </summary>
public class VpnProfileInfo
{
    /// <summary>Gets the VPN profile's ID.</summary>
    public required Guid VpnProfileId { get; init; }

    /// <summary>Gets the VPN profile's name, as the person named it or the access key does.</summary>
    public required string VpnProfileName { get; init; }

    /// <summary>Gets the access key's support ID, which the server's operator knows it by, or <see langword="null"/>.</summary>
    public required string? SupportId { get; init; }

    /// <summary>Gets the text a UI keeps with the VPN profile, or <see langword="null"/>.</summary>
    public required string? CustomData { get; init; }

    /// <summary>Gets a value indicating whether the person chose a premium location.</summary>
    public required bool IsPremiumLocationSelected { get; init; }

    /// <summary>Gets a value indicating whether the VPN profile is premium.</summary>
    public required bool IsPremium { get; init; }

    /// <summary>Gets the access key's ID.</summary>
    public required string TokenId { get; init; }

    /// <summary>Gets the access key's host name and server addresses, partly masked for display.</summary>
    public required IReadOnlyList<string> HostNames { get; init; }

    /// <summary>Gets a value indicating whether the access key's host name is a valid one, which a DNS lookup can find.</summary>
    public required bool IsValidHostName { get; init; }

    /// <summary>Gets a value indicating whether the app brought the VPN profile with it, rather than the person adding it.</summary>
    public required bool IsBuiltIn { get; init; }

    /// <summary>Gets the VPN profile's premium code, with most of it masked, or <see langword="null"/>.</summary>
    public required string? AccessCode { get; init; }

    /// <summary>
    /// Gets the access server's refusal of the profile's premium code, until a connect succeeds or the code changes or is
    /// removed, or <see langword="null"/>.
    /// </summary>
    public required AccessCodeRefusal? AccessCodeRefusal { get; init; }

    /// <summary>Gets the server locations the person may pick, for the client's country.</summary>
    public required IReadOnlyList<ServerLocationItem> LocationInfos { get; init; }

    /// <summary>Gets a value indicating whether the server's policy lets the person go premium, by a premium code or a purchase.</summary>
    public required bool CanGoPremium { get; init; }

    /// <summary>Gets a value indicating whether the server's policy offers a premium trial.</summary>
    public required bool CanTryPremium { get; init; }

    /// <summary>
    /// Gets a value indicating whether a premium code can be typed for this VPN profile: the operator's policy allows it,
    /// and so does the build (<see cref="Premium.AppPremiumOptions.AllowImportAccessCode"/>).
    /// </summary>
    /// <remarks>
    /// It is not the location's <see cref="ServerLocationOptions.PremiumByCode"/>, which is <see langword="false"/> for a
    /// profile that is premium already: that one tells whether a person can go premium, while a change of code is offered to
    /// those who are.
    /// </remarks>
    public required bool CanImportAccessCode { get; init; }

    /// <summary>
    /// Gets a value indicating whether the premium code this device holds may be shown, as the operator's policy alone says.
    /// </summary>
    /// <remarks>
    /// A store that forbids unlocking the app with a typed code does not forbid a buyer to read the code their purchase
    /// made, for the devices that take it: an App Store build shows the code, and offers no box to type one.
    /// </remarks>
    public required bool CanViewAccessCode { get; init; }

    /// <summary>Gets the server's addresses the person gave, used instead of the access key's where enabled, or <see langword="null"/>.</summary>
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public required IPEndPoint[]? CustomServerEndpoints { get; init; }

    /// <summary>Gets a value indicating whether the VPN profile connects to <see cref="CustomServerEndpoints"/>.</summary>
    public required bool IsCustomServerEndpointsEnabled { get; init; }

    /// <summary>
    /// Gets the server location a connect goes to: the one the person chose, else the automatic one, else the first; or
    /// <see langword="null"/> where the VPN profile has none.
    /// </summary>
    public required ServerLocationItem? SelectedLocationInfo { get; init; }

    /// <summary>Gets the access key's policy for the client's country, or for any country, or <see langword="null"/>.</summary>
    public required ClientPolicy? ClientPolicy { get; init; }

    /// <summary>Gets whether the VPN profile's locations have more than one region in a country.</summary>
    /// <param name="countryCode">The country's two-letter code.</param>
    /// <returns><see langword="true"/> where the country's locations have regions to pick.</returns>
    public bool HasMultipleRegion(string countryCode)
    {
        return LocationInfos.Any(x => x.IsNestedCountry && x.CountryCode == countryCode);
    }
}
