using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Dtos;

public class AppSessionInfo
{
    public required AccessInfo? AccessInfo { get; init; }
    public required bool IsUdpChannelSupported { get; init; }
    public required bool IsDnsServersAccepted { get; init; }
    public required ServerLocationInfo? ServerLocationInfo { get; init; }
    public required bool IsPremiumSession { get; init; }
    public required SessionSuppressType SuppressedTo { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] DnsServers { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version ServerVersion { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }
}