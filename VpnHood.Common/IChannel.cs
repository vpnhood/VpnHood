using System;

namespace VpnHood
{
    public interface IChannel : IDisposable
    {
        bool Connected { get; }
        long SentByteCount { get; }
        long ReceivedByteCount { get; }
        void Start();
        event EventHandler OnFinished;
    }
}
