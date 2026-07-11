using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.Sqlite;

// Schema and address conversion for split-ip dbs (meta lives in SplitDb).
// One SELF-DESCRIBING db per source context: three logical range sets (include, exclude, block), each as
// an IPv4 table (INTEGER) and an IPv6 table (16-byte BLOB), plus a meta table. All six tables always exist
// (empty tables are free and spare readers a table-missing path); which sets are populated IS the db's
// semantic — no external action descriptor is needed to interpret the file.
internal static class SplitIpDb
{
    // 3: one range table pair per action (include/exclude/block); the descriptor action is gone
    public const int SchemaVersion = 3;

    public const string CreateTablesSql =
        "CREATE TABLE include_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);" +
        "CREATE TABLE exclude_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);" +
        "CREATE TABLE block_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);" +
        "CREATE TABLE include_v6 (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);" +
        "CREATE TABLE exclude_v6 (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);" +
        "CREATE TABLE block_v6 (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);" +
        SplitDb.CreateMetaTableSql;

    // Indexes are created AFTER bulk insert (building the B-tree once beats maintaining it per row).
    public const string CreateIndexesSql =
        "CREATE INDEX ix_include_v4 ON include_v4(start_ip);" +
        "CREATE INDEX ix_exclude_v4 ON exclude_v4(start_ip);" +
        "CREATE INDEX ix_block_v4 ON block_v4(start_ip);" +
        "CREATE INDEX ix_include_v6 ON include_v6(start_ip);" +
        "CREATE INDEX ix_exclude_v6 ON exclude_v6(start_ip);" +
        "CREATE INDEX ix_block_v6 ON block_v6(start_ip);";

    public static string GetTableName(FilterAction action, bool isV4)
    {
        var set = action switch {
            FilterAction.Include => "include",
            FilterAction.Exclude => "exclude",
            FilterAction.Block => "block",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "No range table exists for this action.")
        };
        return set + (isV4 ? "_v4" : "_v6");
    }

    // 4 big-endian bytes -> unsigned 32-bit value as a positive long (fits SQLite's signed 64-bit INTEGER).
    // Used both when inserting v4 ranges and when querying a v4 address, so ordering is consistent.
    public static long ToV4Key(ReadOnlySpan<byte> bytes) =>
        ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
}
