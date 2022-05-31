using System;

namespace VpnHood.Tunneling;

public class ChannelEventArgs : EventArgs
{
    public ChannelEventArgs(IChannel channel)
    {
        Channel = channel;
    }

    public IChannel Channel { get; set; }
}