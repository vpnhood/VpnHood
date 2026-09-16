using System.Text.Json.Serialization;

namespace VpnHood.App.AvaloniaUI.Browser;

// The one document this head reads on its own - the assets manifest - generated ahead of time,
// as the build is trimmed.
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserJsonContext : JsonSerializerContext;
