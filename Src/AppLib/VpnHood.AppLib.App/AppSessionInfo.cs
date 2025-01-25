using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib;

public class AppSessionInfo
{
    public required AccessInfo? AccessInfo { get; init; }
    public required bool IsUdpChannelSupported { get; init; }
    public required bool IsDnsServersAccepted { get; init; }
    public required ServerLocationInfo? ServerLocationInfo { get; init; }
    public required Version ServerVersion { get; init; }
    public required bool IsPremiumSession { get; init; }
    public required SessionSuppressType SuppressedTo { get; init; }
    public required IPAddress[] DnsServers { get; init; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }

    public static AppSessionInfo Create(SessionInfo sessionInfo)
    {
        return new AppSessionInfo {
            AccessInfo = sessionInfo.AccessInfo,
            IsUdpChannelSupported = sessionInfo.IsUdpChannelSupported,
            IsDnsServersAccepted = sessionInfo.IsDnsServersAccepted,
            ServerLocationInfo = sessionInfo.ServerLocationInfo,
            ServerVersion = sessionInfo.ServerVersion,
            IsPremiumSession = sessionInfo.IsPremiumSession,
            SuppressedTo = sessionInfo.SuppressedTo,
            DnsServers = sessionInfo.DnsServers,
            ClientPublicIpAddress = sessionInfo.ClientPublicIpAddress
        };
    }
}