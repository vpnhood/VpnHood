using System;

namespace VpnHood.Tunneling
{
    public interface ITimeoutItem : IDisposable
    {
        DateTime AccessedTime { get; set; }
        bool IsDisposed { get; }
    }
}