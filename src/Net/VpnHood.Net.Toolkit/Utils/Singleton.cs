namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides a base for a class with one registered instance in the process, which <see cref="Instance"/> gives.
/// </summary>
/// <typeparam name="T">The class itself.</typeparam>
public class Singleton<T> where T : Singleton<T>
{
    private static T? _instance;

    /// <summary>Gets the registered instance.</summary>
    /// <exception cref="InvalidOperationException">No instance is registered.</exception>
    public static T Instance =>
        _instance ?? throw new InvalidOperationException($"{typeof(T).Name} has not been initialized yet.");

    /// <summary>Gets a value indicating whether an instance is registered.</summary>
    public static bool IsInit => _instance != null;

    /// <summary>Initializes a new instance of the class and registers it.</summary>
    /// <param name="register">
    /// <see langword="true"/> to register the instance; <see langword="false"/> for an ordinary one, as tests run
    /// many in one process, which leaves <see cref="Instance"/> as it is.
    /// </param>
    /// <exception cref="InvalidOperationException">An instance is registered already.</exception>
    protected Singleton(bool register = true)
    {
        if (!register)
            return;

        // in one step: of two instances made at the same moment, both could find none registered
        if (Interlocked.CompareExchange(ref _instance, (T)this, null) != null)
            throw new InvalidOperationException($"{typeof(T).Name} is already initialized.");
    }

    /// <summary>
    /// Releases the instance; with <paramref name="disposing"/> and when it is the registered one, unregisters it.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing) {
            // an unregistered instance must not clear another instance's registration
            Interlocked.CompareExchange(ref _instance, null, (T)this);
        }
    }

    /// <summary>Disposes the instance, and unregisters it when it is the registered one.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}