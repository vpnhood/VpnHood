using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

public class TunnelOptions
{
    public required int MaxPacketChannelCount { get; init; } = 8;
    public required int Mtu { get; init; }

    // the session's UDP channel, which the tunnel takes over and disposes; none for a session without UDP
    public IPacketChannel? UdpChannel { get; init; }
}
