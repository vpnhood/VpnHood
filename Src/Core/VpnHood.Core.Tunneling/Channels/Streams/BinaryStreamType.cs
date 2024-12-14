using System.Text.Json.Serialization;

namespace VpnHood.Core.Tunneling.Channels.Streams;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BinaryStreamType
{
    Unknown,
    None,
    Standard
}