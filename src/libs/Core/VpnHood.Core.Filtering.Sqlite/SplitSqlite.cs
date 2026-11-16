namespace VpnHood.Core.Filtering.Sqlite;

/// <summary>
/// Provides the one-time setup of the native SQLite library that <c>Microsoft.Data.Sqlite.Core</c> needs, and the
/// memory the library holds.
/// </summary>
public static class SplitSqlite
{
    private static bool _initialized;
    private static readonly Lock InitLock = new();

    /// <summary>
    /// Registers the native SQLite library of the package's bundle, once in a process; later calls do nothing.
    /// </summary>
    /// <remarks>
    /// The builders and filters of this package call it themselves; call it before opening a
    /// <see cref="Microsoft.Data.Sqlite.SqliteConnection"/> of your own.
    /// </remarks>
    public static void EnsureInitialized()
    {
        lock (InitLock) {
            if (_initialized)
                return;
            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }

    /// <summary>
    /// Gets the bytes the native SQLite library holds now, for all the process's connections
    /// (<c>sqlite3_memory_used</c>).
    /// </summary>
    /// <returns>The bytes; -1 before <see cref="EnsureInitialized"/>, or when the call fails.</returns>
    public static long MemoryUsed()
    {
        try {
            lock (InitLock) {
                if (!_initialized)
                    return -1;
            }

            return SQLitePCL.raw.sqlite3_memory_used();
        }
        catch {
            return -1;
        }
    }
}
