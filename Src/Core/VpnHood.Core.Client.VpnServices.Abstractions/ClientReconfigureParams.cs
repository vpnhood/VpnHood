using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class ClientReconfigureParams
{
    public bool DropUdp { get; set; }
    public ChannelProtocol ChannelProtocol { get; set; }
    public ProxyOptions ProxyNodes { get; set; } = new();
}