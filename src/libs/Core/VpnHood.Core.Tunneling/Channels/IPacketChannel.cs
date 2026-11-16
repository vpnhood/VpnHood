using VpnHood.Net.PacketTransports;

namespace VpnHood.Core.Tunneling.Channels;

/// <summary>Represents a channel that carries a session's packets.</summary>
public interface IPacketChannel : IPacketTransport, IChannel;