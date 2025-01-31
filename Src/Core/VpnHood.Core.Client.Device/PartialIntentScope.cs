namespace VpnHood.Core.Client.Device;

internal class PartialIntentScope : IDisposable
{
    private static int _partialIntentCounter;
    public static bool IsRunning => _partialIntentCounter != 0;

    public PartialIntentScope()
    {
        Interlocked.Increment(ref _partialIntentCounter);
    }
    public void Dispose()
    {
        Interlocked.Decrement(ref _partialIntentCounter);

    }
}