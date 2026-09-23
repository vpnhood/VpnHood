using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions.Ads;

[JsonConverter(typeof(JsonStringEnumConverter<AdType>))]
public enum AdType
{
    InterstitialAd,
    RewardedAd,
    AppOpenAd
}
