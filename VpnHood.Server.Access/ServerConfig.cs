using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class ServerConfig
    {
        public ServerConfig(IPEndPoint[] ipEndPoints)
        {
            IPEndPoints = ipEndPoints;
        }

        [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
        public IPEndPoint[] IPEndPoints { get; set; }

        public int UdpPort { get; set; }
    }
}