using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

// ReSharper disable once ClassNeverInstantiated.Global
internal class TokenVersion
{
    [JsonPropertyName("v")] 
    public int Version { get; set; }
}