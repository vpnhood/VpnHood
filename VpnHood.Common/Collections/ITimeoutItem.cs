using System;

namespace VpnHood.Common.Collections
{
    public interface ITimeoutItem : IDisposable
    {
        DateTime AccessedTime { get; set; }
        bool IsDisposed { get; }
    }
}