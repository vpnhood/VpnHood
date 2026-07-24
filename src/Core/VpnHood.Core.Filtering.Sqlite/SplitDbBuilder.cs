using Microsoft.Data.Sqlite;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Filtering.Sqlite;

// Shared one-shot build core for split dbs (ip and domain): schema creation, bulk-insert transaction,
// meta, index-after-insert, atomic replace, and the staleness check. Schema subclasses (SplitIpDbBuilder,
// SplitDomainDbBuilder) supply the table SQL and the row inserter; their derived classes supply the
// context's business: what identifies the source (GetSourceSignature) and what rows to stream. Builds
// run in the app process (memory is fine there); the resulting file is opened read-only by the VpnService.
// A crash never leaves a half-built db in place — the build targets a temp file that atomically replaces
// the target (VPN connections are exclusive, so the target is never in use).
public abstract class SplitDbBuilder
{
    // Context identity, stored as the db's source_signature meta: must change iff the stored sets would
    // change. Invoked on every EnsureAsync, so it must be cheap (compose of hashes/stat, never parse).
    // Public so callers can derive signature-versioned file names (a changed source gets a NEW file, so
    // a running VpnService can keep the old db open until it swaps); same value EnsureAsync compares.
    public abstract string GetSourceSignature();

    protected abstract int SchemaVersion { get; }
    protected abstract string CreateTablesSql { get; }
    protected abstract string CreateIndexesSql { get; }

    // Stream the context's rows into the db (via the schema's inserter, whose raw statements must be
    // finalized before this returns). Invoked only on the rebuild path, inside the bulk-insert transaction.
    protected abstract Task InsertAsync(SqliteConnection connection, CancellationToken cancellationToken);

    // Reuse the db when its own meta matches the current source signature (no sidecar files), rebuild
    // otherwise. The common case — every connect after the first with an unchanged source — returns at
    // the meta check without touching the source at all.
    public async Task EnsureAsync(string dbPath, CancellationToken cancellationToken)
    {
        if (IsUpToDate(dbPath, GetSourceSignature()))
            return;

        await BuildAsync(dbPath, cancellationToken).Vhc();
    }

    public async Task BuildAsync(string dbPath, CancellationToken cancellationToken)
    {
        SplitSqlite.EnsureInitialized();

        var tempPath = dbPath + ".tmp";
        DeleteDbFiles(tempPath);
        var folderPath = Path.GetDirectoryName(Path.GetFullPath(dbPath))
            ?? throw new ArgumentException($"The db path has no parent folder: {dbPath}", nameof(dbPath));
        Directory.CreateDirectory(folderPath);

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
        Execute(connection, CreateTablesSql);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).Vhc();
        await InsertAsync(connection, cancellationToken).Vhc();

        // meta written in the data transaction, EXCEPT built_complete (set only after indexes exist)
        SplitDb.SetMeta(connection, transaction, SplitDb.KeySchemaVersion, SchemaVersion.ToString());
        SplitDb.SetMeta(connection, transaction, SplitDb.KeySourceSignature, GetSourceSignature());

        // the transaction ends HERE; index creation is deliberately outside it (index-after-bulk-insert)
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        Execute(connection, CreateIndexesSql);

        // finalize: only now the db is complete and queryable
        SplitDb.SetMeta(connection, transaction: null, SplitDb.KeyBuiltComplete, "1");
    }

    private bool IsUpToDate(string dbPath, string sourceSignature)
    {
        if (!File.Exists(dbPath))
            return false;

        try {
            SplitSqlite.EnsureInitialized();
            var connectionString = new SqliteConnectionStringBuilder {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var meta = SplitDb.ReadMeta(connection);
            return meta.TryGetValue(SplitDb.KeyBuiltComplete, out var complete) && complete == "1" &&
                   meta.TryGetValue(SplitDb.KeySchemaVersion, out var schema) &&
                   schema == SchemaVersion.ToString() &&
                   meta.TryGetValue(SplitDb.KeySourceSignature, out var signature) &&
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
