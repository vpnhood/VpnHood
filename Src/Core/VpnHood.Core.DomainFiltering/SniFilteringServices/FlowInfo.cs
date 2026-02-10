using VpnHood.Core.Packets;

namespace VpnHood.Core.DomainFiltering.SniFilteringServices;

internal class FlowInfo
{
    public object? SniState { get; set; }
    public string? DomainName { get; set; }
    public DomainFilterAction? Decision { get; set; }
    public List<IpPacket> BufferedPackets { get; set; } = [];
    public long LastSeenTicks { get; set; }
}
