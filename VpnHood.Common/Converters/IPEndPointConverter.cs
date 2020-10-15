using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Coverters
{
    public class IPEndPointConverter : JsonConverter<IPEndPoint>
    {
        public override IPEndPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString().Split(':');
            return new IPEndPoint(address: IPAddress.Parse(value[0]), int.Parse(value[1]));

        }

        public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
