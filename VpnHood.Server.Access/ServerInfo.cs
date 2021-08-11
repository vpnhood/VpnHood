using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class ServerInfo
    {
        
        [JsonConverter(typeof(VersionConveter))]
        public Version Version { get; set; }
        
        [JsonConverter(typeof(VersionConveter))]
        public Version EnvironmentVersion { get; set; }


        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress LocalIp { get; set; }

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress PublicIp { get; set; }

        public string OsInfo { get; set; }
        public long TotalMemory { get; set; }
        public string MachineName { get; set; }
    }
}
