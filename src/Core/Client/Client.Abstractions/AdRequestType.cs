using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<AdRequestType>))]
public enum AdRequestType
{
    Rewarded,
    Interstitial
}
