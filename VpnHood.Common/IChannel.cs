using System;
using System.Net;
using System.Net.Security;

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
