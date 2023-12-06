namespace VpnHood.Tunneling.Channels;

public class ChannelEventArgs : EventArgs
{
    public required IChannel Channel { get; init; }
}