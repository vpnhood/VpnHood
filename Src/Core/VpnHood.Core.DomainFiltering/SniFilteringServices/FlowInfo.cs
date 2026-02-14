using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering.SniFilteringServices;

internal class FlowInfo
{
    public object? SniState { get; set; }
    public string? DomainName { get; set; }
    public FilterAction? Decision { get; set; }
    public List<IpPacket> BufferedPackets { get; set; } = [];
    public long LastSeenTicks { get; set; }
}
