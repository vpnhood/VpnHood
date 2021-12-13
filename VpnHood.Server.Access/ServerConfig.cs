using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class ServerConfig
    {
        public TrackingOptions TrackingOptions { get; set; } = new();
        public SessionOptions SessionOptions { get; set; } = new();
        public int UdpPort { get; set; }

        [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
        public IPEndPoint[] TcpEndPoints { get; set; }

        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan UpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(120);

        public ServerConfig(IPEndPoint[] tcpEndPoints)
        {
            TcpEndPoints = tcpEndPoints;
        }
    }
}