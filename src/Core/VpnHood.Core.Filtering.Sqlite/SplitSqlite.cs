namespace VpnHood.Core.Filtering.Sqlite;

// Microsoft.Data.Sqlite.Core does not auto-register a native provider; the bundle package supplies
// Batteries_V2.Init(). Call this once before touching any SqliteConnection.
public static class SplitSqlite
{
    private static bool _initialized;
    private static readonly Lock InitLock = new();

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
    /// Live bytes currently held by the native SQLite allocator across ALL connections in the process
    /// (<c>sqlite3_memory_used</c>). -1 when the provider is not initialized or the call fails.
    /// </summary>
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
