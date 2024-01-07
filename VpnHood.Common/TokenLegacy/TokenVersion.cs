using System.Text.Json.Serialization;

namespace VpnHood.Common.TokenLegacy;

internal class TokenVersion
{
    [JsonPropertyName("v")]
    public int Version { get; set; }
}