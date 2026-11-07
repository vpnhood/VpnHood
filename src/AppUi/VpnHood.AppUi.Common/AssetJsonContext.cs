using System.Text.Json.Serialization;

namespace VpnHood.AppUi.Common;

// The heads publish trimmed, so the shapes the store's JSON files deserialize into are declared
// rather than discovered: a language's words or country names, an index that names files, and a theme's manifest.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ThemeManifest))]
internal sealed partial class AssetJsonContext : JsonSerializerContext;
