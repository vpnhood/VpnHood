using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

public class PortRangeConverter : JsonConverter<PortRange>
{
    public override PortRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return PortRange.Parse(reader.GetString() ?? "");
    }

    public override void Write(Utf8JsonWriter writer, PortRange value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
