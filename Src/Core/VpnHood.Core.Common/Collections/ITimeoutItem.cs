namespace VpnHood.Core.Common.Collections;

public interface ITimeoutItem : IDisposable
{
    DateTime LastUsedTime { get; set; }
    bool Disposed { get; }
}