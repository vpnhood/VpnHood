using Microsoft.Data.Sqlite;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// Shared one-shot build core for split dbs (ip and domain): schema creation, bulk-insert transaction,
// meta, index-after-insert, atomic replace, and the staleness check. Schema subclasses (SplitIpDbBuilder,
// SplitDomainDbBuilder) supply the table SQL and the row inserter; their derived classes supply the
// context's business: what identifies the source (GetSourceSignature) and what rows to stream. Builds
// run in the app process (memory is fine there); the resulting file is opened read-only by the VpnService.
// A crash never leaves a half-built db in place — the build targets a temp file that atomically replaces
// the target, which is never in use while its name comes from GetFileName.
/// <summary>
/// Provides the build of a split database: a SQLite file of include, exclude and block sets, which a filter of this
/// package reads.
/// </summary>
/// <remarks>
/// <para>
/// Derive from <see cref="SplitIpDbBuilder"/> or <see cref="SplitDomainDbBuilder"/>, which give the schema. A build
/// writes a new file beside the target, its path plus <c>.tmp</c>, with no journal: the rows in one transaction, the
/// indexes after them, and the mark that the file is complete last. It then moves the new file over the target in
/// one step, so a crash never leaves a half-built database there, nor none.
/// </para>
/// <para>
/// The target must not be open while it is replaced: name it by <see cref="GetFileName"/>, which changes with the
/// source and with the schema, so a running filter keeps the old file until it swaps.
/// </para>
/// </remarks>
public abstract class SplitDbBuilder
{
    /// <summary>
    /// Gets a file name for the database, one that changes with its source and with its schema.
    /// </summary>
    /// <param name="prefix">The start of the name, which tells the context apart, as <c>split-domain</c>.</param>
    /// <returns>
    /// The prefix, a dot, a hash of the schema's version and <see cref="GetSourceSignature"/>, and <c>.db</c>.
    /// </returns>
    /// <remarks>
    /// A build must not replace a file a filter holds open, so a database of another source, or of another release's
    /// schema, takes another name, and the filter swaps to it.
    /// </remarks>
    public string GetFileName(string prefix) =>
        $"{prefix}.{VhUtils.GetHexStringSha256($"{SchemaVersion}:{GetSourceSignature()}", 16)}.db";

    /// <summary>
    /// Gets the identity of the source of the sets, which a build stores in the database: it must change exactly when
    /// the sets would.
    /// </summary>
    /// <returns>The identity, compared as a string.</returns>
    /// <remarks>
    /// <see cref="EnsureAsync"/> calls it every time, so keep it cheap: hashes and file sizes and times, never a
    /// parse of the source.
    /// </remarks>
    public abstract string GetSourceSignature();

    // the schema's, sealed by SplitIpDbBuilder and SplitDomainDbBuilder
    private protected abstract int SchemaVersion { get; }
    private protected abstract string CreateTablesSql { get; }
    private protected abstract string CreateIndexesSql { get; }

    // Stream the context's rows into the db (via the schema's inserter, whose raw statements must be
    // finalized before this returns). Invoked only on the rebuild path, inside the bulk-insert transaction.
    private protected abstract Task InsertAsync(SqliteConnection connection, CancellationToken cancellationToken);

    /// <summary>
    /// Builds the database at a path, unless the file there is complete, of this builder's schema version, and from
    /// the same source signature.
    /// </summary>
    /// <param name="dbPath">The database's path.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// A file that cannot be opened or read counts as out of date. With an unchanged source, it reads the database's
    /// own record of its build and nothing of the source.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="dbPath"/> has no parent folder.</exception>
    /// <exception cref="SqliteException">SQLite refused a row of the build.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task EnsureAsync(string dbPath, CancellationToken cancellationToken)
    {
        if (IsUpToDate(dbPath, GetSourceSignature()))
            return;

        await BuildAsync(dbPath, cancellationToken).Vhc();
    }

    /// <summary>
    /// Builds the database at a path, replacing the file there.
    /// </summary>
    /// <param name="dbPath">The database's path; its folder is made if missing.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// A build that fails, in its new file or in the move, leaves the target's file as it was, and the new file, which
    /// the next build deletes. The target's <c>-wal</c>, <c>-shm</c> and <c>-journal</c> files are deleted before the
    /// move: a database this builder made has none, and one that another writer left with changes only in such a file
    /// loses those changes, even when the move then fails.
    /// </remarks>
    /// <exception cref="IOException">The new file could not be moved to the target's path.</exception>
    /// <exception cref="ArgumentException"><paramref name="dbPath"/> has no parent folder.</exception>
    /// <exception cref="SqliteException">SQLite refused a row of the build.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

        // Atomic replace (target is never open — see GetFileName). Only the old db's side files go
        // first: the old db itself deleted before the move left none at all when the move then failed. Before
        // the move, not after it: for the moment between the two, and for good after a crash in it, the new db
        // would lie beside the old one's journal, which SQLite takes for its own and plays back into it.
        DeleteSideFiles(dbPath);
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
        DeleteSideFiles(dbPath);
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    private static void DeleteSideFiles(string dbPath)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" }) {
            var path = dbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
