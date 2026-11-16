using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Converts an <see cref="IPEndPoint"/> to and from its text form, such as <c>192.168.0.1:443</c> or
/// <c>[::1]:443</c>.
/// </summary>
// ReSharper disable once InconsistentNaming
public class IPEndPointConverter : JsonConverter<IPEndPoint>
{
    /// <inheritdoc/>
    /// <exception cref="FormatException">The value is null or not an endpoint.</exception>
    /// <exception cref="InvalidOperationException">The value is not a string.</exception>
    public override IPEndPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value == null || !IPEndPoint.TryParse(value, out var ipEndPoint))
            throw new FormatException($"An invalid IPEndPoint was specified. Value: {value}.");

        return ipEndPoint;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}