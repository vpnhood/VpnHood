using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.Sessions;

// Which resolvers the session ended up with, and whether they are inside the tunnel.
public class DnsConfig
{
    public required DnsSelection DnsSelection { get; init; }
    public required bool IsIncludedInVpn { get; init; }
    public required bool IsUserSuppressed { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] DnsServers { get; init; }
}
