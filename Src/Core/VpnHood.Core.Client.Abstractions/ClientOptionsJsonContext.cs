using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

// Source-generated serializer context for ClientOptions.
// Reflection-based System.Text.Json hangs inside iOS NetworkExtension AOT sandbox,
// so we use source generation to avoid the hang.
// ToDo: Check is really required for iOS AOT
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ClientOptions))]
public partial class ClientOptionsJsonContext : JsonSerializerContext
{
}
