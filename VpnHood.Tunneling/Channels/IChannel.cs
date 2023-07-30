using System;
using System.Threading.Tasks;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Channels;

public interface IChannel : IAsyncDisposable
{
    string ChannelId { get; }
    bool Connected { get; }
    DateTime LastActivityTime { get; }
    Traffic Traffic { get; }
    void Start();
    ValueTask DisposeAsync(bool graceFul);
}