using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdRequestType
{
    Rewarded,
    Interstitial
}