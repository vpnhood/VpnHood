using Microsoft.Data.Sqlite;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.EndPointManagement.Sqlite;

internal static class SqliteHelper
{
    private static bool _initialized;
    private static readonly Lock InitLock = new();

    // Microsoft.Data.Sqlite.Core does not auto-register a native provider; the bundle package
    // supplies Batteries_V2.Init(). Call this once before touching any SqliteConnection.
    public static void EnsureInitialized()
    {
        lock (InitLock) {
            if (_initialized)
                return;
            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }

    public static async Task ExecuteAsync(SqliteConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync().Vhc();
    }

    public static async Task<object?> ExecuteScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync().Vhc();
    }

    public static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static long? GetNullableInt64(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    public static object ToUnixMs(DateTime? dateTime)
    {
        return dateTime != null
            ? new DateTimeOffset(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : DBNull.Value;
    }

    public static DateTime? FromUnixMs(long? unixMs)
    {
        return unixMs != null ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value).UtcDateTime : null;
    }
}
