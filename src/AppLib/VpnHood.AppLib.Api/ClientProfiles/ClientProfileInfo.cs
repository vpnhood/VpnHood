using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.Api.ClientProfiles;

// A profile as the UIs read it: plain values taken off the stored profile once, so the same object
// is read back by a UI on the other side of the API. The client country is baked in - the policy
// and the locations depend on it - which is why ClientProfileService keeps one per profile and
// region. Built by the app (ClientProfileInfoBuilder); nothing here reads a token.
public class ClientProfileInfo
{
    public required Guid ClientProfileId { get; init; }
    public required string ClientProfileName { get; init; }
    public required string? SupportId { get; init; }
    public required string? CustomData { get; init; }
    public required bool IsPremiumLocationSelected { get; init; }
    public required bool IsPremium { get; init; }
    public required string TokenId { get; init; }
    public required string[] HostNames { get; init; }
    public required bool IsValidHostName { get; init; }
    public required bool IsBuiltIn { get; init; }
    public required string? AccessCode { get; init; }
    public required AccessCodeRefusal? AccessCodeRefusal { get; init; }
    public required ServerLocationItem[] LocationInfos { get; init; }
    public required bool CanGoPremium { get; init; }
    public required bool CanTryPremium { get; init; }

    /// <summary>
    /// May a code be TYPED IN on this profile at all (keyring plan §8)? The operator's policy AND
    /// this build's own capability — one store forbids unlocking with a code entirely, which is why
    /// <see cref="Premium.AppPremiumOptions.AllowImportAccessCode" /> defaults to false.
    /// <para>
    /// Deliberately NOT the location's <c>PremiumByCode</c>: that one returns early for a profile
    /// which is already premium, because it answers "can this person UPGRADE". Change code is only
    /// ever offered to somebody who already HAS premium, so reading it there hides the button from
    /// exactly the people it exists for.
    /// </para>
    /// </summary>
    public required bool CanImportAccessCode { get; init; }

    /// <summary>
    /// May the code this device already holds be SHOWN? The operator's policy alone — never
    /// <see cref="Premium.AppPremiumOptions.AllowImportAccessCode" />, which answers a different question.
    /// A store may forbid unlocking the app with a typed code without forbidding a buyer from
    /// reading the credential their own purchase produced: it is what they carry to their Android
    /// or Windows device, where typing it IS allowed. So an App Store build shows the code and
    /// offers no box to type one — the same person, premium on every device they own.
    /// </summary>
    public required bool CanViewAccessCode { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public required IPEndPoint[]? CustomServerEndpoints { get; init; }

    public required bool IsCustomServerEndpointsEnabled { get; init; }
    public required ServerLocationItem? SelectedLocationInfo { get; init; }
    public required ClientPolicy? ClientPolicy { get; init; }

    public bool HasMultipleRegion(string countryCode)
    {
        return LocationInfos.Any(x => x.IsNestedCountry && x.CountryCode == countryCode);
    }
}
