using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client.Abstractions;

public class SessionInfo
{
    public required AccessInfo? AccessInfo { get; init;}
    public required bool IsUdpChannelSupported { get; init;}
    public required bool IsDnsServersAccepted { get; init;}
    public required ServerLocationInfo? ServerLocationInfo { get; init;}
    public required Version ServerVersion { get; init;}
    public required bool IsPremiumSession { get; init;}
    public required SessionSuppressType SuppressedTo { get; init;}
    public required IPAddress[] DnsServers { get; init; }
    public required string? AccessKey { get; set; } // allow set to let clear
    public required string? ClientCountry { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init;}
}