using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Packets;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

internal class FlowInfo
{
    public object? SniState { get; set; }
    public string? DomainName { get; set; }
    public FilterAction? Decision { get; set; }
    public List<IpPacket> BufferedPackets { get; set; } = [];
    public long LastSeenTicks { get; set; }

    // the connection the verdict is for, where the protocol names one: QUIC's destination connection ID of the
    // client's Initials that decrypted, which another connection on the same addresses and ports does not share
    public byte[]? ConnectionId { get; set; }

    // a connection ID seen on the decided flow that begins no connection, as of the client's later Initials, whose
    // keys are the server's: kept so it costs a decryption once
    public byte[]? RejectedConnectionId { get; set; }
}
