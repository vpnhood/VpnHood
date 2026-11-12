using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Updaters;

/// <summary>Specifies how this version stands: against the store's offer where one is made, else against the feed's release.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VersionStatus>))]
public enum VersionStatus
{
    /// <summary>No release has been read.</summary>
    Unknown,

    /// <summary>This version is the latest, or the release is still in its notification delay.</summary>
    Latest,

    /// <summary>A newer version is out.</summary>
    Old,

    /// <summary>This version is deprecated: it must update.</summary>
    Deprecated
}
