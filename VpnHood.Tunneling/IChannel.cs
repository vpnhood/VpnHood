using System;
using System.Threading.Tasks;

namespace VpnHood.Tunneling;

public interface IChannel : IDisposable
{
    bool Connected { get; }
        
    // ReSharper disable once UnusedMemberInSuper.Global
    DateTime LastActivityTime { get; }
    long SentByteCount { get; }
    long ReceivedByteCount { get; }
    Task Start();
    event EventHandler<ChannelEventArgs> OnFinished;
}