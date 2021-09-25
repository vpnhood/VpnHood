using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Converters
{
    // ReSharper disable once InconsistentNaming
    public class IPEndPointConverter : JsonConverter<IPEndPoint>
    {
        public override IPEndPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Parse(reader.GetString() ?? "");
        }

        public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }

        public static IPEndPoint Parse(string value)
        {
            if (!TryParse(value, out var ipEndPoint))
                throw new FormatException($"Could not parse {nameof(IPEndPoint)} from: {value}!");
            return ipEndPoint;
        }

        public static bool TryParse(string value, [NotNullWhen(true)] out IPEndPoint? ipEndPoint)
        {
            ipEndPoint = null;

            // try Ipv6 [2607:f8b0:4007:811::200e]:443
            var ipV6Parts = value.Split("]:");
            if (ipV6Parts.Length == 2)
            {
                if (ipV6Parts[0][0] != '[') return false; //first character must be [
                if (!IPAddress.TryParse(ipV6Parts[0][1..], out var ipAddress)) return false;
                if (!int.TryParse(ipV6Parts[1], out var port)) return false;
                ipEndPoint = new IPEndPoint(ipAddress, port);
                return true;
            }
            else
            {

                var address = value.Split(':');
                if (address.Length != 2) return false;
                if (!IPAddress.TryParse(address[0], out var ipAddress)) return false;
                if (!int.TryParse(address[1], out var port)) return false;
                ipEndPoint = new IPEndPoint(ipAddress, port);
                return true;
            }
        }
    }
}