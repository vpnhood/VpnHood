using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

/// <summary>Specifies the split by country's mode.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SplitCountryMode>))]
public enum SplitCountryMode
{
    /// <summary>Every country goes through the tunnel.</summary>
    IncludeAll,

    /// <summary>The client's own country goes outside the tunnel.</summary>
    ExcludeMyCountry,

    /// <summary>The listed countries go outside the tunnel.</summary>
    ExcludeList,

    /// <summary>Only the listed countries go through the tunnel.</summary>
    IncludeList
}
