namespace VpnHood.Core.Toolkit.Collections;

public interface ITimeoutItem : IDisposable
{
    DateTime LastUsedTime { get; set; }
    bool Disposed { get; }
}