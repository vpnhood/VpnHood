using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace VpnHood.Core.Filtering.Sqlite;

// Ensures the split-country db at dbPath reflects the given asset + selection, rebuilding only on change.
// Called by the app process before connecting. Change is detected from the db's own meta table (asset hash +
// selection signature + schema version + built_complete flag) — no sidecar files.
public static class SplitIpDbManager
{
    public static async Task EnsureAsync(
        string dbPath,
        Func<ZipArchive> zipArchiveFactory,
        IReadOnlyCollection<string> countryCodes,
        string assetHash,
        CancellationToken cancellationToken)
    {
        var selectionSignature = SplitIpDb.BuildSelectionSignature(countryCodes);
        if (IsUpToDate(dbPath, assetHash, selectionSignature))
            return;

        await SplitIpDbBuilder
            .BuildAsync(dbPath, zipArchiveFactory, countryCodes, assetHash, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsUpToDate(string dbPath, string assetHash, string selectionSignature)
    {
        if (!File.Exists(dbPath))
            return false;

        try {
            SplitIpSqlite.EnsureInitialized();
            var connectionString = new SqliteConnectionStringBuilder {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var meta = ReadMeta(connection);
            return meta.TryGetValue(SplitIpDb.KeyBuiltComplete, out var complete) && complete == "1" &&
                   meta.TryGetValue(SplitIpDb.KeySchemaVersion, out var schema) &&
                   schema == SplitIpDb.SchemaVersion.ToString() &&
                   meta.TryGetValue(SplitIpDb.KeyAssetHash, out var hash) && hash == assetHash &&
                   meta.TryGetValue(SplitIpDb.KeySelectionSignature, out var sig) && sig == selectionSignature;
        }
        catch {
            // missing/corrupt/locked db → rebuild
            return false;
        }
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }
}
