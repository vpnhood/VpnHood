using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Specifies a kind of ad: rewarded or interstitial.
/// </summary>
/// <remarks>In JSON it is a name.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AdRequestType>))]
public enum AdRequestType
{
    /// <summary>A rewarded ad, which the user watches to the end.</summary>
    Rewarded,

    /// <summary>An interstitial ad.</summary>
    Interstitial
}
