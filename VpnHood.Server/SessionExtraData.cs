using System.Text.Json.Serialization;

namespace VpnHood.Server;

public class SessionExtraData
{
    [JsonPropertyName("pv")] public required int ProtocolVersion { get; init; }
}