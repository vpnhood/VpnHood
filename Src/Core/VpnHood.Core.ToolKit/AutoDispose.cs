namespace VpnHood.Core.ToolKit;

public class AutoDispose(Action disposeAction) : IDisposable
{
    public void Dispose()
    {
        disposeAction();
    }
}