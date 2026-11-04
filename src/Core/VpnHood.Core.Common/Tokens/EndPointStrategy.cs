using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Tokens;

// A name or number this side does not know reads as Auto.
[JsonConverter(typeof(EndPointStrategyConverter))]
[JsonEnumFallback(EndPointStrategy.Auto)]
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
                return reader.TryGetInt32(out var number) && Enum.IsDefined((EndPointStrategy)number)
                    ? (EndPointStrategy)number
                    : EndPointStrategy.Auto;

            case JsonTokenType.String: {
                    var name = reader.GetString();

                    return Enum.TryParse<EndPointStrategy>(name, ignoreCase: true, out var value) &&
                           Enum.IsDefined(value)
                        ? value
                        : EndPointStrategy.Auto;
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