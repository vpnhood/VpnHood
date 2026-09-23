namespace VpnHood.Core.Tunneling.Channels;

public enum PacketChannelState
{
    NotStarted,
    Connected,
    Disconnecting,
    Disposed
}