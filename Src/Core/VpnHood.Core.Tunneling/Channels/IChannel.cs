using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Channels;

public interface IChannel : IAsyncDisposable
{
    string ChannelId { get; }
    bool Connected { get; }
    DateTime LastActivityTime { get; }
    Traffic Traffic { get; }
    void Start();
    ValueTask DisposeAsync(bool graceful);
}