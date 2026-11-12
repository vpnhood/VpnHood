using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Ads;

/// <summary>Specifies how a shown ad ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShowAdResult>))]
public enum ShowAdResult
{
    /// <summary>The person closed the ad.</summary>
    Closed,

    /// <summary>The person clicked the ad.</summary>
    Clicked
}
