using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Client.Device
{
    public class IpNetworkConverter : JsonConverter<IpNetwork>
    {
        public override IpNetwork Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => IpNetwork.Parse(reader.GetString());
        public override void Write(Utf8JsonWriter writer, IpNetwork value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
