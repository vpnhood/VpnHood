using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client;

public interface IClientStat
{
    public AccessInfo? AccessInfo { get; }
    public ConnectorStat ConnectorStat { get; }
    public Traffic Speed { get; }
    public Traffic SessionTraffic { get; }
    public Traffic CycleTraffic { get; }
    public Traffic TotalTraffic { get; }
    public int TcpTunnelledCount { get; }
    public int TcpPassthruCount { get; }
    public int DatagramChannelCount { get; }
    public bool IsUdpMode { get; }
    public bool IsUdpChannelSupported { get; }
    public bool IsWaitingForAd { get; }
    public bool IsDnsServersAccepted { get; }
    public ServerLocationInfo? ServerLocationInfo { get; }
    public Version ServerVersion { get; }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress ClientPublicIpAddress { get; }
}