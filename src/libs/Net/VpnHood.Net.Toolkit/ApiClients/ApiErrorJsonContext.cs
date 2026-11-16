using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.ApiClients;

// Read in either casing: .NET's default (TypeName) or a web host's camelCase (typeName), as the app's own API
// writes it. Generated, as the browser page runs trimmed with reflection off.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ApiError))]
internal sealed partial class ApiErrorJsonContext : JsonSerializerContext;
