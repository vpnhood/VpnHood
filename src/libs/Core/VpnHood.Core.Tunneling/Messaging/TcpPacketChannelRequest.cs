namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>Represents a request for a stream that carries the session's packets: a packet channel.</summary>
public class TcpPacketChannelRequest()
    : RequestBase(Messaging.RequestCode.TcpPacketChannel)
{
    /// <summary>Gets the channel's ID, or <see langword="null"/> for the request's ID.</summary>
    public string? ChannelId { get; init; }

    /// <summary>
    /// Gets the IDs of the client's live packet channels, or <see langword="null"/>: the server closes its older ones
    /// that are not listed.
    /// </summary>
    public IReadOnlyList<string>? ActiveChannelIds { get; init; }

    /// <summary>
    /// Gets the sequence number of the client's next UDP datagram, or <see langword="null"/>: the server takes the ones
    /// before it as older, so a late one cannot turn the session back to UDP.
    /// </summary>
    public ulong? UdpSendSequenceNumber { get; init; }
}