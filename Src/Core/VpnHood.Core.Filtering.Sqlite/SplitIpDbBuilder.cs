using System.IO.Compression;
using Microsoft.Data.Sqlite;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// One-shot builder for the split-country IP db. Runs in the app process (memory is fine there); the resulting
// file is opened read-only by the VpnService. Builds to a temp file then atomically replaces the target, so a
// crash never leaves a half-built db in place (VPN connections are exclusive, so the target is never in use).
public static class SplitIpDbBuilder
{
    private const int CancellationCheckInterval = 20000;

    public static async Task BuildAsync(
        string dbPath,
        Func<ZipArchive> zipArchiveFactory,
        IReadOnlyCollection<string> countryCodes,
        string assetHash,
        CancellationToken cancellationToken)
    {
        SplitIpSqlite.EnsureInitialized();

        var selectionSignature = SplitIpDb.BuildSelectionSignature(countryCodes);
        var tempPath = dbPath + ".tmp";
        DeleteDbFiles(tempPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = tempPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false // keep no handle on the file so we can move/delete it afterward
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString)) {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // disposable build → durability irrelevant; go fast
            Execute(connection, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-16000;");
            Execute(connection, SplitIpDb.CreateTablesSql);

            await using var zip = zipArchiveFactory();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).Vhc();

            // Hot loop uses raw SQLitePCL statements on the connection's handle (prepare once, bind/step/reset
            // per row). The SqliteCommand path costs ~30µs/row on Android (Mono) vs ~1µs/row raw — measured on
            // an emulator: all-countries build 16.3s → 0.65s. Same handle, so the rows join the transaction above.
            var db = connection.Handle!;
            using var insertV4 = PrepareRaw(db, "INSERT INTO range_v4 (start_ip, end_ip) VALUES (?, ?)");
            using var insertV6 = PrepareRaw(db, "INSERT INTO range_v6 (start_ip, end_ip) VALUES (?, ?)");

            var rowCount = 0;
            foreach (var countryCode in countryCodes) {
                var entry = zip.GetEntry($"{countryCode.ToLowerInvariant()}.ips");
                if (entry is null)
                    continue; // unknown country code → nothing to add

                await using var stream = await entry.OpenAsync(cancellationToken);
                foreach (var (start, end) in IpRangeOrderedList.DeserializeRaw(stream)) {
                    if (start.Length == 4) {
                        SQLitePCL.raw.sqlite3_bind_int64(insertV4, 1, SplitIpDb.ToV4Key(start));
                        SQLitePCL.raw.sqlite3_bind_int64(insertV4, 2, SplitIpDb.ToV4Key(end));
                        StepReset(db, insertV4);
                    }
                    else {
                        SQLitePCL.raw.sqlite3_bind_blob(insertV6, 1, start);
                        SQLitePCL.raw.sqlite3_bind_blob(insertV6, 2, end);
                        StepReset(db, insertV6);
                    }

                    if (++rowCount % CancellationCheckInterval == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                }
            }

            // meta written in the data transaction, EXCEPT built_complete (set only after indexes exist)
            SplitIpDb.SetMeta(connection, transaction, SplitIpDb.KeySchemaVersion, SplitIpDb.SchemaVersion.ToString());
            SplitIpDb.SetMeta(connection, transaction, SplitIpDb.KeyAssetHash, assetHash);
            SplitIpDb.SetMeta(connection, transaction, SplitIpDb.KeySelectionSignature, selectionSignature);

            // the transaction ends HERE; index creation is deliberately outside it (index-after-bulk-insert)
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            Execute(connection, SplitIpDb.CreateIndexesSql);

            // finalize: only now the db is complete and queryable
            SplitIpDb.SetMeta(connection, transaction: null, SplitIpDb.KeyBuiltComplete, "1");
        }

        SqliteConnection.ClearAllPools();

        // atomic replace (target is never open — connections are exclusive)
        DeleteDbFiles(dbPath);
        File.Move(tempPath, dbPath, overwrite: true);
    }

    private static SQLitePCL.sqlite3_stmt PrepareRaw(SQLitePCL.sqlite3 db, string sql)
    {
        CheckRc(db, SQLitePCL.raw.sqlite3_prepare_v2(db, sql, out var statement));
        return statement;
    }

    private static void StepReset(SQLitePCL.sqlite3 db, SQLitePCL.sqlite3_stmt statement)
    {
        CheckRc(db, SQLitePCL.raw.sqlite3_step(statement));
        SQLitePCL.raw.sqlite3_reset(statement);
    }

    private static void CheckRc(SQLitePCL.sqlite3 db, int rc)
    {
        if (rc is SQLitePCL.raw.SQLITE_OK or SQLitePCL.raw.SQLITE_DONE or SQLitePCL.raw.SQLITE_ROW)
            return;
        throw new SqliteException($"SQLite error {rc}: {SQLitePCL.raw.sqlite3_errmsg(db).utf8_to_string()}", rc);
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
