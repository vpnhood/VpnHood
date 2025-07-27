using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

internal class TokenVersion
{
    [JsonPropertyName("v")] 
    public int Version { get; set; }
}