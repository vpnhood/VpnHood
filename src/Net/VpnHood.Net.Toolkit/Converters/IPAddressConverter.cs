using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Converts an <see cref="IPAddress"/> to and from its text form, such as <c>192.168.0.1</c> or <c>::1</c>.
/// </summary>
// ReSharper disable once InconsistentNaming
public class IPAddressConverter : JsonConverter<IPAddress>
{
    /// <inheritdoc/>
    /// <exception cref="FormatException">The value is not an IP address.</exception>
    /// <exception cref="InvalidOperationException">The value is not a string.</exception>
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return IPAddress.Parse(reader.GetString() ?? "");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}