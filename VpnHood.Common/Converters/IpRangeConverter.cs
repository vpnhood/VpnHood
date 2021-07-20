using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Converters
{
    public class IPEndPointConverter : JsonConverter<IPEndPoint>
    {
        public override IPEndPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString());
        public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());

        public static IPEndPoint Parse(string value)
        {
            if (!TryParse(value, out var ipEndPoint))
                throw new ArgumentException($"Could not parse {value} to an {nameof(IPEndPoint)}!");
            return ipEndPoint;
        }

        public static bool TryParse(string value, out IPEndPoint ipEndPoint)
        {
            ipEndPoint = null;
            var addr = value.Split(':');
            if (addr.Length != 2) return false;
            if (!IPAddress.TryParse(addr[0], out var ipAddress)) return false;
            if (!int.TryParse(addr[1], out var port)) return false;
            ipEndPoint = new IPEndPoint(ipAddress, port);
            return true;
        }
    }
}
