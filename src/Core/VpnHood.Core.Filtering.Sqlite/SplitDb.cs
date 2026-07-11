using Microsoft.Data.Sqlite;

namespace VpnHood.Core.Filtering.Sqlite;

// Meta table shared by every split db (ip and domain): identifies the schema, the source the db was built
// from, and whether the build finished. The schema-specific parts (range/domain tables) live in SplitIpDb
// and SplitDomainDb.
internal static class SplitDb
{
    public const string KeySourceSignature = "source_signature";
    public const string KeySchemaVersion = "schema_version";
    public const string KeyBuiltComplete = "built_complete";

    public const string CreateMetaTableSql =
        "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);";

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
