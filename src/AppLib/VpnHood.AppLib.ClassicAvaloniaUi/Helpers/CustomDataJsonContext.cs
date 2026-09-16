using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Helpers;

// the heads publish trimmed, so the shapes UserCustomData serializes are declared
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class CustomDataJsonContext : JsonSerializerContext;
