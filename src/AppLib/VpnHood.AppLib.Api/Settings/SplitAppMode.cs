using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

/// <summary>Specifies the split by app's mode.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SplitAppMode>))]
public enum SplitAppMode
{
    /// <summary>Every app goes through the tunnel.</summary>
    All,

    /// <summary>The listed apps go outside the tunnel.</summary>
    Exclude,

    /// <summary>Only the listed apps go through the tunnel.</summary>
    Include
}
