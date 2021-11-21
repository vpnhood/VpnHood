using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class ServerConfig
    {
        public ServerConfig(IPEndPoint[] tcpEndPoints)
        {
            TcpEndPoints = tcpEndPoints;
        }

        [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
        public IPEndPoint[] TcpEndPoints { get; set; }

        public int UdpPort { get; set; }
        public int UpdateStatusInterval { get; set; } = 120;
    }
}