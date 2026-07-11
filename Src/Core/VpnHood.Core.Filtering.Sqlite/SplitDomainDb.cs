using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.Sqlite;

// Schema for split-domain dbs (meta lives in SplitDb) — the string twin of SplitIpDb.
// One SELF-DESCRIBING db per source context: three domain sets (include, exclude, block), one table each,
// plus a meta table. All three tables always exist; which sets are populated IS the db's semantic.
// Rows hold the canonical DomainUtils form: normalized and part-inverted ("com.google" for "google.com" /
// "*.google.com"), so an entry is an ordinal prefix of every subdomain it matches.
internal static class SplitDomainDb
{
    public const int SchemaVersion = 1;

    // "_domains" suffix keeps the names clear of SQL keywords (EXCLUDE) and reads as the set it is
    public const string CreateTablesSql =
        "CREATE TABLE include_domains (domain TEXT NOT NULL);" +
        "CREATE TABLE exclude_domains (domain TEXT NOT NULL);" +
        "CREATE TABLE block_domains (domain TEXT NOT NULL);" +
        SplitDb.CreateMetaTableSql;

    // Indexes are created AFTER bulk insert (building the B-tree once beats maintaining it per row).
    public const string CreateIndexesSql =
        "CREATE INDEX ix_include_domains ON include_domains(domain);" +
        "CREATE INDEX ix_exclude_domains ON exclude_domains(domain);" +
        "CREATE INDEX ix_block_domains ON block_domains(domain);";

    public static string GetTableName(FilterAction action) => action switch {
        FilterAction.Include => "include_domains",
        FilterAction.Exclude => "exclude_domains",
        FilterAction.Block => "block_domains",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "No domain table exists for this action.")
    };
}
