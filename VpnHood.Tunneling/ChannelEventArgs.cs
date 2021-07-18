using System;

namespace VpnHood.Tunneling
{
    public class ChannelEventArgs : EventArgs
    {
        public IChannel Channel { get; set; }
        public ChannelEventArgs(IChannel channel) => Channel = channel;

    }
}
