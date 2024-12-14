namespace VpnHood.Core.Tunneling.Channels;

public class ChannelEventArgs : EventArgs
{
    public required IChannel Channel { get; init; }
}