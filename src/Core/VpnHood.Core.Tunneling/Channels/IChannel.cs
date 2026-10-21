using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Channels;

// Its graceful close is its normal end, which hands a reusable connection back to its pool; the quick one closes its
// connection at once
public interface IChannel : IDisposable, IAsyncDisposable
{
    string ChannelId { get; }
    DateTime LastActivityTime { get; }
    Traffic Traffic { get; }
    void Start();
    PacketChannelState State { get; }
}