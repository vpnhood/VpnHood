using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

[JsonConverter(typeof(EndPointStrategyConverter))]
public enum EndPointStrategy
{
    Auto = 0,
    DnsFirst = 1,
    IpFirst = 2,
    DnsOnly = 3,
    IpOnly = 4,

    // obsolete values
    [Obsolete]
    TokenFirst = 2,

    [Obsolete]
    TokenOnly = 4
}

// legacy converter to support both numeric and string representations.
// Deprecated in >= 787
public sealed class EndPointStrategyConverter : JsonConverter<EndPointStrategy>
{
    public override EndPointStrategy Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType) {
            case JsonTokenType.Number:
                return (EndPointStrategy)reader.GetInt32();

            case JsonTokenType.String: {
                    var name = reader.GetString();

                    if (Enum.TryParse<EndPointStrategy>(name, ignoreCase: true, out var value))
                        return value;

                    throw new JsonException($"Invalid {nameof(EndPointStrategy)} value: {name}");
                }

            default:
                throw new JsonException($"Unexpected token {reader.TokenType}");
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        EndPointStrategy value,
        JsonSerializerOptions options)
    {
        // always write numeric id
        writer.WriteNumberValue((int)value);
    }
}