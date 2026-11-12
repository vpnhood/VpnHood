using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

// The numbers are the stored values: this is written to settings.json, so they must never move. The
// obsolete engine aliases (TokenFirst, TokenOnly) are not repeated here - they were duplicates of 2
// and 4 and read back as those.

/// <summary>
/// Specifies how a client orders the addresses of an access key's server: those its host name resolves to, and those
/// the access key lists.
/// </summary>
/// <remarks>
/// In JSON it is written as a number, and read as a number or a name: a number reads as it is, even one this version
/// does not know, and a name it does not know fails to read.
/// </remarks>
[JsonConverter(typeof(EndPointStrategyConverter))]
public enum EndPointStrategy
{
    /// <summary>
    /// The server token's own strategy, as a client's setting; as the token's, the host name's addresses first.
    /// </summary>
    Auto = 0,

    /// <summary>The host name's addresses first, then the listed ones.</summary>
    DnsFirst = 1,

    /// <summary>The listed addresses first, then the host name's.</summary>
    IpFirst = 2,

    /// <summary>The host name's addresses alone; with no valid host name, as <see cref="Auto"/>.</summary>
    DnsOnly = 3,

    /// <summary>The listed addresses alone; with none listed, as <see cref="DnsOnly"/>.</summary>
    IpOnly = 4
}

/// <summary>
/// Converts an <see cref="EndPointStrategy"/> to and from JSON: it writes the number, and reads a number or a name.
/// </summary>
public sealed class EndPointStrategyConverter : JsonConverter<EndPointStrategy>
{
    /// <inheritdoc/>
    /// <remarks>A number reads as it is; a name this version does not know fails.</remarks>
    /// <exception cref="FormatException">The number is not a whole number that fits in an <see cref="int"/>.</exception>
    /// <exception cref="JsonException">
    /// The token is a name this version does not know, or neither a number nor a string.
    /// </exception>
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

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EndPointStrategy value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}
