using System.Text.Json.Serialization;

namespace VpnHood.AppHosting.Common;

// The heads publish trimmed, so the theme's manifest is declared rather than discovered. The store writes it in
// camelCase and ThemeManifest is PascalCase: without the case-insensitive match no colour is read, and the read only
// warns.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ThemeManifest))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext;
