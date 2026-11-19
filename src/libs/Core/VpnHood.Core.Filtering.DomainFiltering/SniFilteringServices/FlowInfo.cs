using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Packets;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

internal class FlowInfo
{
    // the read of a connection's name while it goes on: its state, and the packets held for its verdict
    public object? SniState { get; set; }
    public List<IpPacket> BufferedPackets { get; set; } = [];

    // The flow's verdict, which answers every packet that begins no connection; null until the flow's first connection
    // is read. Past their first packets the connections of one flow are not told apart, so it is the most protective
    // of their verdicts (PacketSniFilteringService)
    public FilterAction? Decision { get; set; }

    // the name of the connection whose verdict the flow last took
    public string? DomainName { get; set; }
    public long LastSeenTicks { get; set; }

    // The connection read last to its end, where the protocol names one: QUIC's destination connection ID of the
    // client's Initials that decrypted, which another connection on the same addresses and ports does not share. Its
    // first packets sent again are known by it, and are not read again
    public byte[]? ConnectionId { get; set; }

    // what that connection's first packets left with, which is not always the flow's verdict: a blocked one's are
    // dropped, and those of a name with no entry go to the IP rules
    public FilterAction ConnectionAction { get; set; }
}
