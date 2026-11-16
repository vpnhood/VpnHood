namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides an <see cref="IDisposable"/> that runs an action when disposed, for a <c>using</c> scope.
/// </summary>
/// <param name="disposeAction">The action.</param>
public class AutoDispose(Action disposeAction) : IDisposable
{
    /// <summary>Runs the action, at each call.</summary>
    public void Dispose()
    {
        disposeAction();
    }
}