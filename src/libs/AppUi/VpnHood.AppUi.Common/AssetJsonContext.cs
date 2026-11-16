using System.Text.Json.Serialization;

namespace VpnHood.AppUi.Common;

// The heads publish trimmed, so the shapes the store's JSON files deserialize into are declared
// rather than discovered: a language's words or country names, and an index that names files.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AssetJsonContext : JsonSerializerContext;
