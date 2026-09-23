using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

// How an access key's server addresses are resolved. The numbers are the stored values: this is
// written to settings.json, so they must never move. The obsolete engine aliases (TokenFirst,
// TokenOnly) are not repeated here - they were duplicates of 2 and 4 and read back as those.
[JsonConverter(typeof(EndPointStrategyConverter))]
public enum EndPointStrategy
{
    Auto = 0,
    DnsFirst = 1,
    IpFirst = 2,
    DnsOnly = 3,
    IpOnly = 4
}

// Reads both the numeric form and the name a build before 787 may have written; always writes the
// number, exactly as the engine's converter does. Serialization plumbing, not a rule about the app.
public sealed class EndPointStrategyConverter : JsonConverter<EndPointStrategy>
{
    public override EndPointStrategy Read(ref Utf8JsonReader reader, Type typeToConvert,
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

    public override void Write(Utf8JsonWriter writer, EndPointStrategy value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}
