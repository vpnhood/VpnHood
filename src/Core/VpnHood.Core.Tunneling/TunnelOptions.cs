using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

/// <summary>Represents the options of a <see cref="Tunnel"/>.</summary>
public class TunnelOptions
{
    /// <summary>Gets the most packet channels the tunnel holds at once.</summary>
    public required int MaxPacketChannelCount { get; init; } = 8;

    /// <summary>Gets the largest packet the tunnel's other end takes, as the packet is.</summary>
    public required int Mtu { get; init; }

    /// <summary>
    /// Gets the session's UDP channel, which the tunnel takes over and disposes, or <see langword="null"/> for a session
    /// without UDP.
    /// </summary>
    public IPacketChannel? UdpChannel { get; init; }
}
