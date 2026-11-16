using Microsoft.Data.Sqlite;

namespace VpnHood.Core.Filtering.Sqlite;

// Raw SQLitePCL statement helpers shared by the bulk inserters (prepare once, bind/step/reset per row).
internal static class SplitDbRaw
{
    public static SQLitePCL.sqlite3_stmt PrepareRaw(SQLitePCL.sqlite3 db, string sql)
    {
        CheckRc(db, SQLitePCL.raw.sqlite3_prepare_v2(db, sql, out var statement));
        return statement;
    }

    public static void StepReset(SQLitePCL.sqlite3 db, SQLitePCL.sqlite3_stmt statement)
    {
        CheckRc(db, SQLitePCL.raw.sqlite3_step(statement));
        SQLitePCL.raw.sqlite3_reset(statement);
    }

    public static void CheckRc(SQLitePCL.sqlite3 db, int rc)
    {
        if (rc is SQLitePCL.raw.SQLITE_OK or SQLitePCL.raw.SQLITE_DONE or SQLitePCL.raw.SQLITE_ROW)
            return;
        throw new SqliteException($"SQLite error {rc}: {SQLitePCL.raw.sqlite3_errmsg(db).utf8_to_string()}", rc);
    }
}
