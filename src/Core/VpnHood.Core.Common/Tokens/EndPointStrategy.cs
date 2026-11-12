using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// Specifies how a client orders the addresses of a server token: those its host name resolves to, and those it
/// lists (<see cref="ServerToken.HostEndPoints"/>).
/// </summary>
/// <remarks>
/// In JSON it is written as a number, and read as a number or a name; one this version does not know reads as
/// <see cref="Auto"/>.
/// </remarks>
[JsonConverter(typeof(EndPointStrategyConverter))]
[JsonEnumFallback(EndPointStrategy.Auto)]
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
    IpOnly = 4,

    /// <summary>The former name of <see cref="IpFirst"/>.</summary>
    [Obsolete]
    TokenFirst = 2,

    /// <summary>The former name of <see cref="IpOnly"/>.</summary>
    [Obsolete]
    TokenOnly = 4
}

/// <summary>
/// Converts an <see cref="EndPointStrategy"/> to and from JSON: it writes the number, and reads a number or a name,
/// ignoring case; one it does not know reads as <see cref="EndPointStrategy.Auto"/>.
/// </summary>
// Deprecated in >= 787
internal sealed class EndPointStrategyConverter : JsonConverter<EndPointStrategy>
{
    /// <inheritdoc/>
    /// <exception cref="JsonException">The JSON value is neither a number nor a string.</exception>
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

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        EndPointStrategy value,
        JsonSerializerOptions options)
    {
        // always write numeric id
        writer.WriteNumberValue((int)value);
    }
}