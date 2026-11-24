using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

/// <summary>Represents the options of a <see cref="Tunnel"/>.</summary>
public class TunnelOptions
{
    /// <summary>Gets the most packet channels the tunnel holds at once.</summary>
    public required int MaxPacketChannelCount { get; init; }

    /// <summary>Gets the largest packet the tunnel's other end takes, as the packet is.</summary>
    public required int Mtu { get; init; }

    /// <summary>
    /// Gets the session's UDP channel, which the tunnel takes over and disposes, or <see langword="null"/> for a session
    /// without UDP.
    /// </summary>
    public IPacketChannel? UdpChannel { get; init; }

    /// <summary>
    /// Gets how long a stream forwarding channel may pass no bytes either way before the tunnel closes it, or
    /// <see langword="null"/>, the default, for no limit.
    /// </summary>
    /// <remarks>
    /// The tunnel looks at each run of its cleanup, every <see cref="VpnHood.Net.Toolkit.Jobs.JobOptions.DefaultInterval"/>.
    /// </remarks>
    public TimeSpan? StreamForwardingIdleTimeout { get; init; }
}
