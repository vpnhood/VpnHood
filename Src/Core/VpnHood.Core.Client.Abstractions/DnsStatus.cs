using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;


namespace VpnHood.Core.Client.Abstractions;

public class DnsStatus
{
    public required DnsSelection DnsSelection { get; init; }
    public required bool IsIncludedInVpn { get; init; }
    public required bool IsUserSuppressed { get; init; }
    
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] DnsServers { get; init; }
}