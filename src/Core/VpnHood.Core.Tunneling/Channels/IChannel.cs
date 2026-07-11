using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Channels;

public interface IChannel : IDisposable
{
    string ChannelId { get; }
    DateTime LastActivityTime { get; }
    Traffic Traffic { get; }
    void Start();
    PacketChannelState State { get; }
}