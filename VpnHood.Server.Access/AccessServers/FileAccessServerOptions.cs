using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.AccessServers
{
    public class FileAccessServerOptions
    {
        [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
        public IPEndPoint[] TcpEndPoints { get; set; } = { new(IPAddress.Any, 443), new(IPAddress.IPv6Any, 443) };

        public int UdpPort { get; set; }

        public string? SslCertificatesPassword { get; set; }
    }
}