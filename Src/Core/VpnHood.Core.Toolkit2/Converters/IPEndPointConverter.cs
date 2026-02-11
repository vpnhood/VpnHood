using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Toolkit.Converters;

// ReSharper disable once InconsistentNaming
public class IPEndPointConverter : JsonConverter<IPEndPoint>
{
    public override IPEndPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value == null || !IPEndPoint.TryParse(value, out var ipEndPoint))
            throw new FormatException($"An invalid IPEndPoint was specified. Value: {value}.");

        return ipEndPoint;
    }

    public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}