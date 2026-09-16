using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Assets;

// The heads publish trimmed, so the shape a locale file deserializes into is declared rather than
// discovered.
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocaleJsonContext : JsonSerializerContext;
