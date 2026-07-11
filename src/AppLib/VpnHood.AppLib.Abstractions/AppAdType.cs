using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<AppAdType>))]
public enum AppAdType
{
    InterstitialAd,
    RewardedAd,
    AppOpenAd
}
