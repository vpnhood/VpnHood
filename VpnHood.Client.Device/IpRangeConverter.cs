using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Client.Device
{
    public class IpRangeConverter : JsonConverter<IpRange>
    {
        public override IpRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => IpRange.Parse(reader.GetString());
        public override void Write(Utf8JsonWriter writer, IpRange value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
