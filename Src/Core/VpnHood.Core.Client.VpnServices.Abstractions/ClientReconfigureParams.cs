using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class ClientReconfigureParams
{
    public required bool DropQuic { get; set; }
    public required bool DropUdp { get; set; }
    public required bool UseTcpProxy { get; set; }
    public required ChannelProtocol ChannelProtocol { get; set; }
    public required ProxyOptions ProxyOptions { get; set; } = new();
}