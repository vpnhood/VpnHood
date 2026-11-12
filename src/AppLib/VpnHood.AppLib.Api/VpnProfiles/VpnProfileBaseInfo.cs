using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.VpnProfiles;

/// <summary>Represents the current VPN profile as the state carries it: what the main screen needs.</summary>
public class VpnProfileBaseInfo
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

    /// <summary>Gets a value indicating whether the VPN profile has a premium code.</summary>
    public required bool HasAccessCode { get; init; }

    /// <summary>
    /// Gets the access server's refusal of the profile's premium code, until a connect succeeds or the code changes or is
    /// removed, or <see langword="null"/>.
    /// </summary>
    public required AccessCodeRefusal? AccessCodeRefusal { get; init; }

    /// <summary>Gets a value indicating whether the server's policy lets the person go premium, by a premium code or a purchase.</summary>
    public required bool CanGoPremium { get; init; }

    /// <summary>Gets a value indicating whether the server's policy offers a premium trial.</summary>
    public required bool CanTryPremium { get; init; }

    /// <summary>Gets a value indicating whether a premium code may be typed in on the VPN profile: the server's policy and this build both allow it.</summary>
    public required bool CanImportAccessCode { get; init; }

    /// <summary>Gets a value indicating whether the VPN profile's premium code may be shown, as the server's policy allows.</summary>
    public required bool CanViewAccessCode { get; init; }

    /// <summary>
    /// Gets the server location a connect goes to: the one the person chose, else the automatic one, else the first; or
    /// <see langword="null"/> where the VPN profile has none.
    /// </summary>
    public required ServerLocationItem? SelectedLocationInfo { get; init; }

    /// <summary>Gets the server's addresses the person gave, used instead of the access key's where enabled, or <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? CustomServerEndpoints { get; init; }

    /// <summary>Gets a value indicating whether the VPN profile connects to <see cref="CustomServerEndpoints"/>.</summary>
    public required bool IsCustomServerEndpointsEnabled { get; init; }
}