using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class ServerInfo
    {
        [JsonConstructor]
        public ServerInfo(Version version)
        {
            Version = version;
        }

        [JsonConverter(typeof(VersionConverter))]
        public Version Version { get; set; }

        [JsonConverter(typeof(VersionConverter))]
        public Version? EnvironmentVersion { get; set; }

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress? LocalIp { get; set; }

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress? PublicIp { get; set; }

        public string? OsInfo { get; set; }
        public long TotalMemory { get; set; }
        public string? MachineName { get; set; }
    }
}