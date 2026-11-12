using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions.Ads;

/// <summary>Specifies a kind of ad.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdType>))]
public enum AdType
{
    /// <summary>A full-screen ad between two of the app's screens.</summary>
    InterstitialAd,

    /// <summary>A full-screen ad the person watches for a reward.</summary>
    RewardedAd,

    /// <summary>A full-screen ad shown as the app opens.</summary>
    AppOpenAd
}
