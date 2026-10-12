using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Updaters;

// What a check the person asked for found: the update, which the store's own screen or the feed's
// notice shows; nothing newer; or no answer to tell either, the store that has the update being out
// of reach included.
[JsonConverter(typeof(JsonStringEnumConverter<VersionCheckResult>))]
public enum VersionCheckResult
{
    Failed,
    Latest,
    UpdateAvailable
}
