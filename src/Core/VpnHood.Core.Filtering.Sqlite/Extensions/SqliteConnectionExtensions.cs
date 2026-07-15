using Microsoft.Data.Sqlite;

namespace VpnHood.Core.Filtering.Sqlite.Extensions;

internal static class SqliteConnectionExtensions
{
    // Handle is declared nullable (null until the connection opens); callers that need the raw SQLitePCL
    // handle assert the open-connection invariant loudly through this instead of suppressing with "!"
    public static SQLitePCL.sqlite3 GetRequiredHandle(this SqliteConnection connection) =>
        connection.Handle ??
        throw new InvalidOperationException("The connection must be open to expose its SQLite handle.");
}
