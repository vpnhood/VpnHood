using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;

namespace VpnHood.Core.Server;

public class VirtualIpBundle
{
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpV4 { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpV6 { get; set; }
}