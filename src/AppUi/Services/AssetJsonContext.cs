using System.Text.Json.Serialization;

namespace VpnHood.AppUi.Services;

// The heads publish trimmed, so the shapes the store's JSON files deserialize into are declared
// rather than discovered: a language's words, and an index that names files.
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AssetJsonContext : JsonSerializerContext;
