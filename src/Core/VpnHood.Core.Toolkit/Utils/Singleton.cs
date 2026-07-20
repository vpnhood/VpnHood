namespace VpnHood.Core.Toolkit.Utils;

public class Singleton<T> where T : Singleton<T>
{
    private static T? _instance;

    public static T Instance =>
        _instance ?? throw new InvalidOperationException($"{typeof(T).Name} has not been initialized yet.");

    public static bool IsInit => _instance != null;

    // register: false creates an ordinary unregistered object (used by tests to run many
    // instances in one process); Instance stays untouched and would still throw if accessed.
    protected Singleton(bool register = true)
    {
        if (!register)
            return;

        if (IsInit) throw new InvalidOperationException($"{typeof(T).Name} is already initialized.");
        _instance = (T?)this;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) {
            // an unregistered instance must not clear another instance's registration
            if (_instance == this)
                _instance = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}