using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Updaters;

/// <summary>
/// Specifies what a check the person asked for found: the update, which the store's own screen or the feed's notice
/// shows; nothing newer; or no answer to tell either, a store out of reach included.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VersionCheckResult>))]
public enum VersionCheckResult
{
    /// <summary>The check found no answer, or the store's update screen failed to open.</summary>
    Failed,

    /// <summary>Nothing newer is out, or the feed's release is still in its notification delay.</summary>
    Latest,

    /// <summary>An update is out.</summary>
    UpdateAvailable
}
