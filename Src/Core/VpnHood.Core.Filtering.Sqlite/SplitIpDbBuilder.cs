using Microsoft.Data.Sqlite;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// Base one-shot builder for split-ip dbs: owns the shared build core (schema, bulk-insert transaction,
// meta, index-after-insert, atomic replace) and the staleness check. Derived classes supply only the
// context's business: what identifies the source (BuildSourceSignature) and how to stream its ranges
// (InsertRangesAsync). Builds run in the app process (memory is fine there); the resulting file is opened
// read-only by the VpnService. A crash never leaves a half-built db in place — the build targets a temp
// file that atomically replaces the target (VPN connections are exclusive, so the target is never in use).
public abstract class SplitIpDbBuilder
{
    // Context identity, stored as the db's source_signature meta: must change iff the stored set would
    // change. Invoked on every EnsureAsync, so it must be cheap (compose of hashes/stat, never parse).
    protected abstract string BuildSourceSignature();

    // Stream the context's ranges into the db. Invoked only on the rebuild path.
    protected abstract Task InsertRangesAsync(SplitIpDbInserter inserter, CancellationToken cancellationToken);

    // Reuse the db when its own meta matches the current source signature (no sidecar files), rebuild
    // otherwise. The common case — every connect after the first with an unchanged source — returns at
    // the meta check without touching the source at all.
    public async Task EnsureAsync(string dbPath, CancellationToken cancellationToken)
    {
        if (IsUpToDate(dbPath, BuildSourceSignature()))
            return;

        await BuildAsync(dbPath, cancellationToken).Vhc();
    }

    public async Task BuildAsync(string dbPath, CancellationToken cancellationToken)
    {
        SplitIpSqlite.EnsureInitialized();

        var tempPath = dbPath + ".tmp";
        DeleteDbFiles(tempPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // every handle on the temp file is released when this returns, so it can be moved
        await BuildTempDbAsync(tempPath, cancellationToken).Vhc();
        SqliteConnection.ClearAllPools();

        // atomic replace (target is never open — connections are exclusive)
        DeleteDbFiles(dbPath);
        File.Move(tempPath, dbPath, overwrite: true);
    }

    private async Task BuildTempDbAsync(string tempPath, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = tempPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false // keep no handle on the file so we can move/delete it afterward
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // disposable build → durability irrelevant; go fast
        Execute(connection, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-16000;");
        Execute(connection, SplitIpDb.CreateTablesSql);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).Vhc();

        // the inserter's raw statements run on the connection's handle, so the rows join the transaction;
        // declared after the connection so its statements are finalized before the connection closes
        using var inserter = new SplitIpDbInserter(connection.Handle!, cancellationToken);
        await InsertRangesAsync(inserter, cancellationToken).Vhc();

        // meta written in the data transaction, EXCEPT built_complete (set only after indexes exist)
        SplitIpDb.SetMeta(connection, transaction, SplitIpDb.KeySchemaVersion, SplitIpDb.SchemaVersion.ToString());
        SplitIpDb.SetMeta(connection, transaction, SplitIpDb.KeySourceSignature, BuildSourceSignature());

        // the transaction ends HERE; index creation is deliberately outside it (index-after-bulk-insert)
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        Execute(connection, SplitIpDb.CreateIndexesSql);

        // finalize: only now the db is complete and queryable
        SplitIpDb.SetMeta(connection, transaction: null, SplitIpDb.KeyBuiltComplete, "1");
    }

    private static bool IsUpToDate(string dbPath, string sourceSignature)
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

            var meta = SplitIpDb.ReadMeta(connection);
            return meta.TryGetValue(SplitIpDb.KeyBuiltComplete, out var complete) && complete == "1" &&
                   meta.TryGetValue(SplitIpDb.KeySchemaVersion, out var schema) &&
                   schema == SplitIpDb.SchemaVersion.ToString() &&
                   meta.TryGetValue(SplitIpDb.KeySourceSignature, out var signature) &&
                   signature == sourceSignature;
        }
        catch {
            // missing/corrupt/locked db → rebuild
            return false;
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DeleteDbFiles(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) {
            var path = dbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
