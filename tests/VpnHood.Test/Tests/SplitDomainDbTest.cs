using Microsoft.Data.Sqlite;
using VpnHood.AppLogic.Api.App;
using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.AppLogic.Api.SplitTunneling;
using VpnHood.AppLogic.App.Premium;
using VpnHood.AppLogic.App.Services;
using VpnHood.AppLogic.App.Settings;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Net.Toolkit.Storages;
using VpnHood.Net.Toolkit.Utils;

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
        return [.. domains];
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
        // 2 since a final dot is dropped, so a db that kept one on its row is rebuilt
        Assert.AreEqual("2", ReadMeta(dbPath, "schema_version"));
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

    // The most specific entry decides, whichever set holds it, so an entry under another is an exception to it; only
    // between entries of the same domain do the sets' order decide. Before, the set decided: a block or an exclude of
    // a parent took every subdomain whatever else was listed for it.
    [TestMethod]
    public async Task The_most_specific_entry_decides()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-filter", "most-specific.db");
        await new DomainListDbBuilder(() => "sig-1",
            includesFactory: () => ["mail.google.com", "a.b.example.com", "same.org"],
            excludesFactory: () => ["google.com", "business.facebook.com", "same.org"],
            blocksFactory: () => ["facebook.com", "b.example.com", "same.org"]
        ).BuildAsync(dbPath, TestCt);
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        Assert.AreEqual(FilterAction.Include, filter.Process("mail.google.com"));
        Assert.AreEqual(FilterAction.Include, filter.Process("x.mail.google.com"));
        Assert.AreEqual(FilterAction.Exclude, filter.Process("www.google.com"));
        Assert.AreEqual(FilterAction.Exclude, filter.Process("business.facebook.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("www.facebook.com"));
        Assert.AreEqual(FilterAction.Include, filter.Process("a.b.example.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("c.b.example.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("www.same.org"));
    }

    // Each entry under another is an exception to it, however deep; under a blocked parent, a child in two sets still
    // goes by the sets' order
    [TestMethod]
    public async Task Exceptions_nest_and_the_sets_order_holds_at_each_level()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-filter", "nested.db");
        await new DomainListDbBuilder(() => "sig-1",
            includesFactory: () => ["child.example.com", "b.example.com"],
            excludesFactory: () => ["child.example.com", "c.b.example.com"],
            blocksFactory: () => ["example.com", "d.c.b.example.com"]
        ).BuildAsync(dbPath, TestCt);
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        Assert.AreEqual(FilterAction.Exclude, filter.Process("x.child.example.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("a.example.com"));
        Assert.AreEqual(FilterAction.Include, filter.Process("x.b.example.com"));
        Assert.AreEqual(FilterAction.Exclude, filter.Process("x.c.b.example.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("x.d.c.b.example.com"));
    }

    // A fully qualified name ends with a dot, as a resolver may hand it on, and is the same name. Before, the dot stayed
    // on the row: an entry with it matched no name asked without it, and a name with it missed every entry without it
    [TestMethod]
    public async Task A_final_dot_is_the_same_name()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-domain-filter", "final-dot.db");
        await new DomainListDbBuilder(() => "sig-1",
            includesFactory: () => ["mail.example.com."],
            blocksFactory: () => ["example.com", "."]
        ).BuildAsync(dbPath, TestCt);
        using var filter = new SqliteDomainFilter(next: null, dbPath);

        CollectionAssert.AreEqual(new[] { "com.example.mail" }, ReadDomains(dbPath, "include_domains"));
        CollectionAssert.AreEqual(new[] { "com.example" }, ReadDomains(dbPath, "block_domains"),
            "a dot alone is no name");
        Assert.AreEqual(FilterAction.Include, filter.Process("mail.example.com"));
        Assert.AreEqual(FilterAction.Include, filter.Process("x.mail.example.com."));
        Assert.AreEqual(FilterAction.Block, filter.Process("www.example.com."));
        Assert.AreEqual(FilterAction.Default, filter.Process("."));
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

        // and the next stage decides alone
        using var chained = new SqliteDomainFilter(new StubFilter(FilterAction.Block), dbPath);
        Assert.AreEqual(FilterAction.Block, chained.Process("example.com"));
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
        public Task Reconfigure(CancellationToken cancellationToken) => Task.CompletedTask; // nothing to re-read
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
        var settingsService = new AppSettingsService(new Storage(new FileSystemStorageProvider(), storagePath),
            remoteSettingsUrl: null);
        var service = new SplitDomainService(settingsService, new AllowAllPremiumFeatures());

        // the service owns its whole activity decision: toggle off ⇒ inactive, nothing built
        Assert.IsNull(await service.EnsureSplitDomainDb(storagePath, TestCt));
        settingsService.UserSettings.SplitTunneling.UseDomain = true;

        // empty/missing sources leave every set empty, which is a no-op gate (routes identically to no filter)
        var dbPath = await service.EnsureSplitDomainDb(storagePath, TestCt);
        Assert.IsNotNull(dbPath);
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
        settingsService.SplitDomainSettings.Excludes = "exclude.com\n; a comment line\nexclude.org";
        settingsService.SplitDomainSettings.Blocks = "block.com";
        var dbPath2 = await service.EnsureSplitDomainDb(storagePath, TestCt);
        Assert.IsNotNull(dbPath2);
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
        Assert.IsNotNull(dbPath3);
        using (var filter = new SqliteDomainFilter(next: null, dbPath3))
            Assert.AreEqual(FilterAction.Default, filter.Process("block.com"));
    }

    // The release before named a database by its source alone, so a database of the older schema lay at the name this
    // release gave it, held open by the running VPN: the rebuild there was refused on Windows, and on unix left the VPN
    // on the old file, as its path did not change. A new schema takes a new name, as a new source does.
    [TestMethod]
    public async Task A_database_of_an_older_schema_is_not_replaced_while_open()
    {
        var storagePath = CreateStoragePath();
        var settingsService = CreateSettingsService(storagePath);
        settingsService.UserSettings.SplitTunneling.UseDomain = true;
        settingsService.SplitDomainSettings.Blocks = "block.com";
        var service = new SplitDomainService(settingsService, new AllowAllPremiumFeatures());
        var dbFolder = Path.Combine(storagePath, "domain-filters");

        var signature = settingsService.SplitDomainSettings.GetSignature();
        var oldDbPath = Path.Combine(dbFolder, $"split-domain.{VhUtils.GetHexStringSha256(signature, 16)}.db");
        await new DomainListDbBuilder(() => signature, blocksFactory: () => ["block.com"]).BuildAsync(oldDbPath, TestCt);
        WriteMeta(oldDbPath, "schema_version", "1");
        using var runningFilter = new SqliteDomainFilter(next: null, oldDbPath);

        var dbPath = await service.EnsureSplitDomainDb(dbFolder, TestCt);
        Assert.IsNotNull(dbPath);
        Assert.AreNotEqual(oldDbPath, dbPath);
        Assert.AreEqual(FilterAction.Block, runningFilter.Process("block.com"));
        using var filter = new SqliteDomainFilter(next: null, dbPath);
        Assert.AreEqual(FilterAction.Block, filter.Process("block.com"));
    }

    // An entry matches its subdomains already, so a leading "*." changed nothing, and the filters take any other * as a
    // letter no name has: such an entry matched nothing while it looked like a rule. The lists before the refused one
    // are not saved either.
    [TestMethod]
    [DataRow("*.example.com")]
    [DataRow("*ads.example.com")]
    [DataRow("mail.*.example.com")]
    [DataRow("*.*.example.com")]
    [DataRow("*")]
    public void A_domain_with_a_star_anywhere_but_its_start_is_refused(string domain)
    {
        var settings = CreateSettingsService(CreateStoragePath()).SplitDomainSettings;
        settings.Set(new SplitDomains { Includes = "old.com", Excludes = "old.org", Blocks = "" });

        // in each list after the first, so that list's own check is what keeps the ones before it unsaved; the error
        // names the entry and why, for the UI to say it in the person's language
        var exception = Assert.Throws<SplitEntryException>(() =>
            settings.Set(new SplitDomains { Includes = "new.com", Excludes = $"new.org\n{domain}", Blocks = "" }));
        Assert.AreEqual(nameof(SplitEntryError.Asterisk), exception.Data["Reason"]);
        Assert.AreEqual(domain, exception.Data["Entry"]);
        StringAssert.Contains(exception.Message, domain);
        Assert.Throws<SplitEntryException>(() =>
            settings.Set(new SplitDomains { Includes = "new.com", Excludes = "new.org", Blocks = $"block.com\n{domain}" }));
        Assert.AreEqual("old.com", settings.Includes);
        Assert.AreEqual("old.org", settings.Excludes);
        Assert.AreEqual("", settings.Blocks);
    }

    // Only a save checks: a list stored before, or edited by hand, is built as it is, rather than a connect that
    // fails. A leading "*." still means its domain, and a * inside still matches nothing, as they always did.
    [TestMethod]
    public async Task A_list_stored_with_a_star_still_builds()
    {
        var storagePath = CreateStoragePath();
        var settingsService = CreateSettingsService(storagePath);
        settingsService.UserSettings.SplitTunneling.UseDomain = true;
        var service = new SplitDomainService(settingsService, new AllowAllPremiumFeatures());
        var listFolder = Path.Combine(storagePath, "splits", "domains");
        Directory.CreateDirectory(listFolder);
        await File.WriteAllTextAsync(Path.Combine(listFolder, "blocks.txt"),
            "block.com\n*.legacy.org\n*ads.example.com", TestCt);

        var dbPath = await service.EnsureSplitDomainDb(storagePath, TestCt);
        Assert.IsNotNull(dbPath);
        using var filter = new SqliteDomainFilter(next: null, dbPath);
        Assert.AreEqual(FilterAction.Block, filter.Process("block.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("legacy.org"));
        Assert.AreEqual(FilterAction.Block, filter.Process("www.legacy.org"));
        Assert.AreEqual(FilterAction.Default, filter.Process("xads.example.com"));
    }

    // A list stored before the lists took no *, or edited by hand: a leading "*." meant what the domain alone means, so
    // it goes as the list is read, and the person meets no refusal for it at the next save, which writes the list
    // without it. The lines end as the parser reads them: at \r\n, \r or \n.
    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\r")]
    [DataRow("\n")]
    public void A_stored_lists_leading_star_goes_as_it_is_read(string lineBreak)
    {
        var storagePath = CreateStoragePath();
        var listFolder = Path.Combine(storagePath, "splits", "domains");
        Directory.CreateDirectory(listFolder);
        File.WriteAllText(Path.Combine(listFolder, "blocks.txt"),
            string.Join(lineBreak, "*.legacy.org", "  *.spaced.org # *.a comment", "*.", "plain.org"));
        var settings = CreateSettingsService(storagePath).SplitDomainSettings;

        var expected = string.Join(lineBreak, "legacy.org", "  spaced.org # *.a comment", "", "plain.org");
        Assert.AreEqual(expected, settings.Blocks);
        settings.Set(settings.Get());
        Assert.AreEqual(expected, File.ReadAllText(Path.Combine(listFolder, "blocks.txt")));
    }

    // Only where the leading "*." goes without a change of what the entry matches: with another *, or with white space
    // after it, the entry matched nothing, and without the "*." it would match example.com. Such an entry stays as it
    // is, for the save to name.
    [TestMethod]
    [DataRow("*.*.example.com")]
    [DataRow("*. example.com")]
    [DataRow("*.\texample.com")]
    [DataRow("*ads.example.com")]
    public void A_stored_entry_that_matched_nothing_keeps_its_star(string entry)
    {
        var storagePath = CreateStoragePath();
        var listFolder = Path.Combine(storagePath, "splits", "domains");
        Directory.CreateDirectory(listFolder);
        File.WriteAllText(Path.Combine(listFolder, "blocks.txt"), $"plain.org\n{entry}");
        var settings = CreateSettingsService(storagePath).SplitDomainSettings;

        Assert.AreEqual($"plain.org\n{entry}", settings.Blocks);
        var exception = Assert.Throws<SplitEntryException>(() => settings.Set(settings.Get()));
        Assert.AreEqual(entry.Trim(), exception.Data["Entry"]);
    }

    private string CreateStoragePath()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, $"split-domain-{Guid.NewGuid()}");
        Directory.CreateDirectory(storagePath);
        return storagePath;
    }

    private static AppSettingsService CreateSettingsService(string storagePath)
    {
        return new AppSettingsService(new Storage(new FileSystemStorageProvider(), storagePath), remoteSettingsUrl: null);
    }

    private sealed class AllowAllPremiumFeatures : IPremiumFeatureChecker
    {
        public bool IsPremiumFeatureAllowed(AppFeature feature) => true;
    }
}
