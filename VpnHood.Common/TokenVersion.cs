using System.Text.Json.Serialization;

namespace VpnHood.Common;

internal class TokenVersion
{
    [JsonPropertyName("v")]
    public int Version { get; set; }
}