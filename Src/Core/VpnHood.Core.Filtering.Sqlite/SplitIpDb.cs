using Microsoft.Data.Sqlite;

namespace VpnHood.Core.Filtering.Sqlite;

// Shared schema, meta keys/accessors and address conversion for the split-country IP db.
// One db per selection context; two range tables (IPv4 as INTEGER, IPv6 as 16-byte BLOB) + a meta table.
internal static class SplitIpDb
{
    public const int SchemaVersion = 1;

    public const string KeyAssetHash = "asset_hash";
    public const string KeySelectionSignature = "selection_signature";
    public const string KeySchemaVersion = "schema_version";
    public const string KeyBuiltComplete = "built_complete";

    public const string CreateTablesSql =
        "CREATE TABLE range_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);" +
        "CREATE TABLE range_v6 (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);" +
        "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);";

    // Indexes are created AFTER bulk insert (building the B-tree once beats maintaining it per row).
    public const string CreateIndexesSql =
        "CREATE INDEX ix_range_v4 ON range_v4(start_ip);" +
        "CREATE INDEX ix_range_v6 ON range_v6(start_ip);";

    // 4 big-endian bytes -> unsigned 32-bit value as a positive long (fits SQLite's signed 64-bit INTEGER).
    // Used both when inserting v4 ranges and when querying a v4 address, so ordering is consistent.
    public static long ToV4Key(ReadOnlySpan<byte> bytes) =>
        ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];

    // Canonical selection signature: distinct, upper-cased country codes, ordinal-sorted, comma-joined.
    // Mode is intentionally NOT part of the signature (db content is mode-independent — see docs plan).
    public static string BuildSelectionSignature(IEnumerable<string> countryCodes) =>
        string.Join(',', countryCodes
            .Select(c => c.ToUpperInvariant())
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal));

    public static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public static void SetMeta(SqliteConnection connection, SqliteTransaction? transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value)";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
