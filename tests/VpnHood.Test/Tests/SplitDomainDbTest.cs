using Microsoft.Data.Sqlite;
using VpnHood.AppLib.Services;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;

namespace VpnHood.Test.Tests;

[TestClass]
public class SplitDomainDbTest : TestBase
{
    private static long CountRows(string dbPath, string table)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }
                .ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    private static string[] ReadDomains(string dbPath, string table)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }
                .ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT domain FROM {table} ORDER BY domain";
        using var reader = command.ExecuteReader();
        var domains = new List<string>();
        while (reader.Read())
            domains.Add(reader.GetString(0));
        return domains.ToArray();
    }

    private static string? ReadMeta(string dbPath, string key)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }
                .ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $k";
        command.Parameters.AddWithValue("$k", key);
        return command.ExecuteScalar() as string;
    }

    private static void WriteMeta(string dbPath, string key, string value)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }
                .ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($k, $v)";
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", value);
        command.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    [TestMethod]
    public async Task DomainList_build_creates_sets_and_meta()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-db", "split-domain.db");
        await new DomainListDbBuilder(() => "sig-1",
            includesFactory: () => ["google.com", "*.Include.ORG", "  ", ""],
            excludesFactory: () => ["exclude.com"]
        ).BuildAsync(dbPath, TestCt);

        Assert.IsTrue(File.Exists(dbPath));

        // rows are stored in the canonical form: normalized (lower-case, "*." stripped) and part-inverted;
        // blank entries are dropped
        CollectionAssert.AreEqual(new[] { "com.google", "org.include" }, ReadDomains(dbPath, "include_domains"));
        CollectionAssert.AreEqual(new[] { "com.exclude" }, ReadDomains(dbPath, "exclude_domains"));
        Assert.AreEqual(0, CountRows(dbPath, "block_domains"), "the untargeted sets must stay empty");

        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("sig-1", ReadMeta(dbPath, "source_signature"));
        Assert.AreEqual("1", ReadMeta(dbPath, "schema_version"));
    }

    [TestMethod]
    public async Task DomainList_ensure_reuses_by_signature_and_parses_only_on_rebuild()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-db", "reuse.db");
        var signature = "sig-1";
        var domainsFactoryCalls = 0;

        IReadOnlyList<string> DomainsFactory()
        {
            domainsFactoryCalls++;
            return ["google.com"];
        }

        // ReSharper disable once AccessToModifiedClosure
        var dbBuilder = new DomainListDbBuilder(() => signature, includesFactory: DomainsFactory);

        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.AreEqual(1, domainsFactoryCalls);

        // same signature → reuse; the source must not be parsed at all
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.AreEqual(1, domainsFactoryCalls, "must not invoke the domains factory when the db is up to date");

        // changed signature → rebuild
        signature = "sig-2";
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.AreEqual(2, domainsFactoryCalls);
        Assert.AreEqual("sig-2", ReadMeta(dbPath, "source_signature"));
    }

    [TestMethod]
    public async Task Ensure_rebuilds_on_stale_schema_incomplete_build_or_corrupt_db()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-db", "staleness.db");
        var dbBuilder = new DomainListDbBuilder(() => "sig-1", includesFactory: () => ["google.com"]);

        // schema version mismatch (older/newer app) → rebuild
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        WriteMeta(dbPath, "schema_version", "0");
        WriteMeta(dbPath, "sentinel", "keep");
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild on schema version mismatch");

        // incomplete build (crash before indexes) → rebuild
        WriteMeta(dbPath, "built_complete", "0");
        WriteMeta(dbPath, "sentinel", "keep");
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when built_complete is not set");

        // corrupt file → rebuild without throwing
        await File.WriteAllTextAsync(dbPath, "not a sqlite db", TestCt);
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"), "must recover from a corrupt db");
        Assert.AreEqual(1, CountRows(dbPath, "include_domains"));
    }

    private async Task<string> BuildDbAsync(string name, FilterAction action, params string[] domains)
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-filter", name + ".db");
        await new DomainListDbBuilder(() => "sig-1",
            includesFactory: action is FilterAction.Include ? () => domains : null,
            excludesFactory: action is FilterAction.Exclude ? () => domains : null,
            blocksFactory: action is FilterAction.Block ? () => domains : null
        ).BuildAsync(dbPath, TestCt);
        return dbPath;
    }

    [TestMethod]
    public async Task Filter_include_set_is_the_override_lane()
    {
        var dbPath = await BuildDbAsync("include", FilterAction.Include, "google.com");
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        // member ⇒ Include: unlike the ip gates, the domain include set DOES return Include — it forces
        // the domain through the tunnel past any ip-gate veto
        Assert.AreEqual(FilterAction.Include, filter.Process("google.com"));
        Assert.AreEqual(FilterAction.Include, filter.Process("WWW.GooGle.COM"), "matching is case-insensitive");

        // non-member ⇒ Default: the include set does NOT veto other domains (an ip gate may still decide)
        Assert.AreEqual(FilterAction.Default, filter.Process("example.com"));
        Assert.IsFalse(filter.IsEmpty);
    }

    [TestMethod]
    public async Task Filter_exclude_set_bypasses_members()
    {
        var dbPath = await BuildDbAsync("exclude", FilterAction.Exclude, "exclude.com");
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        Assert.AreEqual(FilterAction.Exclude, filter.Process("exclude.com"));
        Assert.AreEqual(FilterAction.Exclude, filter.Process("api.exclude.com"));
        Assert.AreEqual(FilterAction.Default, filter.Process("example.com"));
    }

    [TestMethod]
    public async Task Filter_block_set_drops_members()
    {
        var dbPath = await BuildDbAsync("block", FilterAction.Block, "block.com");
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        Assert.AreEqual(FilterAction.Block, filter.Process("block.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("deep.sub.block.com"));
        Assert.AreEqual(FilterAction.Default, filter.Process("example.com"));
    }

    [TestMethod]
    public async Task Filter_matches_subdomains_but_not_parents()
    {
        var dbPath = await BuildDbAsync("wildcard", FilterAction.Block, "sub.example.com", "*.wild.org");
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        // every entry matches itself and its subdomains (implicit wildcard, "*." spelling is equivalent)
        Assert.AreEqual(FilterAction.Block, filter.Process("sub.example.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("www.sub.example.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("wild.org"));
        Assert.AreEqual(FilterAction.Block, filter.Process("a.b.wild.org"));

        // an entry must never match its PARENT or an unrelated sibling
        Assert.AreEqual(FilterAction.Default, filter.Process("example.com"));
        Assert.AreEqual(FilterAction.Default, filter.Process("other.example.com"));
        Assert.AreEqual(FilterAction.Default, filter.Process("not-wild.org"), "label boundary must be respected");
    }

    [TestMethod]
    public async Task Filter_finds_ancestor_behind_more_specific_sibling()
    {
        // ordinally, "com.google.mail" sorts between "com.google" and "com.google.www": a single
        // greatest-prefix seek would land on the sibling and miss the ancestor — the label walk must not
        var dbPath = await BuildDbAsync("sibling", FilterAction.Block, "google.com", "mail.google.com");
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        Assert.AreEqual(FilterAction.Block, filter.Process("www.google.com"),
            "the google.com entry must match even though mail.google.com sorts closer");
    }

    [TestMethod]
    public async Task Filter_set_precedence_is_block_exclude_include()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-filter", "precedence.db");
        await new DomainListDbBuilder(() => "sig-1",
            includesFactory: () => ["multi.com", "include.com"],
            excludesFactory: () => ["multi.com", "exclude.multi.com"],
            blocksFactory: () => ["block.multi.com"]
        ).BuildAsync(dbPath, TestCt);
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        Assert.AreEqual(FilterAction.Exclude, filter.Process("multi.com"), "exclude wins over include");
        Assert.AreEqual(FilterAction.Block, filter.Process("block.multi.com"), "block wins over exclude");
        Assert.AreEqual(FilterAction.Include, filter.Process("include.com"));
    }

    [TestMethod]
    public async Task Filter_empty_db_is_a_no_op_gate()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-filter", "empty.db");
        await new DomainListDbBuilder(() => "sig-1").BuildAsync(dbPath, TestCt);
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        // all sets empty ⇒ no constraint: always Default (the db is never consulted on the hot path)
        Assert.IsTrue(filter.IsEmpty);
        Assert.AreEqual(FilterAction.Default, filter.Process("example.com"));
    }

    [TestMethod]
    public async Task Filter_own_decision_preempts_next()
    {
        var dbPath = await BuildDbAsync("chain", FilterAction.Include, "include.com");
        using var filter = new SqliteDomainFilter(new StubFilter(FilterAction.Block), dbPath);

        // own sets are consulted FIRST (mirroring StaticDomainFilter): the include override must not be
        // second-guessed by an inner gate
        Assert.AreEqual(FilterAction.Include, filter.Process("include.com"));

        // no own decision → defer to next
        Assert.AreEqual(FilterAction.Block, filter.Process("example.com"));

        // no domain → nothing to match; the gate stays out of it entirely
        Assert.AreEqual(FilterAction.Default, filter.Process(null));
        Assert.AreEqual(FilterAction.Default, filter.Process("   "));
    }

    private sealed class StubFilter(FilterAction action) : IDomainFilter
    {
        public event EventHandler? Changed { add { } remove { } } // verdicts never change
        public bool IsEmpty => false; // always has a verdict
        public FilterAction Process(string? domain) => action;
        public void Reconfigure() { } // nothing external to re-read
        public void Dispose() { }
    }

    [TestMethod]
    public async Task Filter_pipe_dispose_releases_db_file()
    {
        // Regression: the pipe is disposed from its outermost stage only (NetFilter → CachedDomainFilter →
        // SqliteDomainFilter). A broken link leaks the SQLite connections, keeps the db file locked, and
        // fails the next rebuild with "file is being used by another process".
        var dbPath = await BuildDbAsync("dispose-chain", FilterAction.Include, "include.com");

        // opting out of ownership must leave the inner filter untouched
        var innerFilter = new SqliteDomainFilter(next: null, dbPath);
        new CachedDomainFilter(innerFilter, TimeSpan.FromMinutes(60), autoDisposeNextFilter: false).Dispose();
        Assert.AreEqual(FilterAction.Include, innerFilter.Process("include.com"),
            "inner filter must survive when the wrapper does not own it");

        // the client's pipe shape (outermost first) owns the whole chain by default
        var pipe = new CachedDomainFilter(innerFilter, TimeSpan.FromMinutes(60));
        Assert.AreEqual(FilterAction.Include, pipe.Process("include.com"));
        pipe.Dispose();

        // deleting must succeed: dispose released every handle down the chain
        File.Delete(dbPath);
        Assert.IsFalse(File.Exists(dbPath));
    }

    [TestMethod]
    public async Task SplitDomain_service_builds_db_and_detects_changes()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, "split-domain-service");
        Directory.CreateDirectory(storagePath);
        var settingsService = new AppSettingsService(storagePath, remoteSettingsUrl: null, debugMode: true);
        var service = new SplitDomainService(settingsService);

        // the UseSplitDomain gate lives in the caller; empty/missing sources leave every set empty,
        // which is a no-op gate (routes identically to no filter)
        var dbPath = await service.EnsureSplitDomainDb(storagePath, TestCt);
        StringAssert.Contains(Path.GetFileName(dbPath), "split-domain.",
            "the file name must carry the context and its source signature");
        using (var filter = new SqliteDomainFilter(next: null, dbPath))
            Assert.IsTrue(filter.IsEmpty);

        // unchanged sources → same signature → SAME file (reused, not rebuilt)
        Assert.AreEqual(dbPath, await service.EnsureSplitDomainDb(storagePath, TestCt),
            "an unchanged source must keep the same versioned file name");

        // the sets mirror the source files (comments stripped by the parser). A changed source gets a
        // NEW file name, so a running service could keep the old db open until it swaps.
        settingsService.SplitDomainSettings.Includes = "include.com # tunnel me";
        settingsService.SplitDomainSettings.Excludes = "exclude.com\n; a comment line\n*.exclude.org";
        settingsService.SplitDomainSettings.Blocks = "block.com";
        var dbPath2 = await service.EnsureSplitDomainDb(storagePath, TestCt);
        Assert.AreNotEqual(dbPath, dbPath2, "a changed source must build under a new versioned file name");

        using (var filter = new SqliteDomainFilter(next: null, dbPath2)) {
            Assert.AreEqual(FilterAction.Include, filter.Process("include.com"));
            Assert.AreEqual(FilterAction.Exclude, filter.Process("exclude.com"));
            Assert.AreEqual(FilterAction.Exclude, filter.Process("www.exclude.org"));
            Assert.AreEqual(FilterAction.Block, filter.Process("block.com"));
            Assert.AreEqual(FilterAction.Default, filter.Process("example.com"));
        }

        // source file change → signature change → rebuild with the new sets
        settingsService.SplitDomainSettings.Blocks = string.Empty;
        var dbPath3 = await service.EnsureSplitDomainDb(storagePath, TestCt);
        using (var filter = new SqliteDomainFilter(next: null, dbPath3))
            Assert.AreEqual(FilterAction.Default, filter.Process("block.com"));
    }
}
