using System.IO.Compression;
using System.Net;
using Microsoft.Data.Sqlite;
using VpnHood.AppLib.Services;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class SplitIpDbTest : TestBase
{
    private static byte[] CreateIpsZip(Dictionary<string, IpRange[]> countries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (code, ranges) in countries) {
                var entry = zip.CreateEntry($"{code.ToLower()}.ips");
                using var entryStream = entry.Open();
                ranges.ToOrderedList().Serialize(entryStream);
            }
        }

        return ms.ToArray();
    }

    private static SplitCountryDbBuilder CreateCountryBuilder(byte[] zipBytes, string[] countryCodes,
        string assetHash, FilterAction action = FilterAction.Include) =>
        new(() => new ZipArchive(new MemoryStream(zipBytes)), countryCodes, assetHash, action);

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
    public async Task Country_build_creates_ranges_and_meta()
    {
        // disjoint, non-adjacent ranges so ToOrderedList() does not merge them (predictable counts)
        var zipBytes = CreateIpsZip(new Dictionary<string, IpRange[]> {
            ["US"] = [IpRange.Parse("1.0.0.0 - 1.0.0.255"), IpRange.Parse("3.0.0.0 - 3.0.0.255")],
            ["TR"] = [IpRange.Parse("5.0.0.0 - 5.0.0.255"), IpRange.Parse("2001:db8:: - 2001:db8::ffff")]
        });

        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "split-country.db");
        await CreateCountryBuilder(zipBytes, ["US", "TR"], "hash-1").BuildAsync(dbPath, TestCt);

        Assert.IsTrue(File.Exists(dbPath));
        Assert.AreEqual(3, CountRows(dbPath, "include_v4"), "expected 3 IPv4 ranges (US:2 + TR:1)");
        Assert.AreEqual(1, CountRows(dbPath, "include_v6"), "expected 1 IPv6 range");
        Assert.AreEqual(0, CountRows(dbPath, "exclude_v4"), "the untargeted sets must stay empty");
        Assert.AreEqual(0, CountRows(dbPath, "block_v4"), "the untargeted sets must stay empty");

        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("hash-1|Include|TR,US", ReadMeta(dbPath, "source_signature"),
            "asset hash + target set + sorted+upper codes");
        Assert.AreEqual("3", ReadMeta(dbPath, "schema_version"));

        // the same codes in the exclude set are a different db (the set is part of the content)
        await CreateCountryBuilder(zipBytes, ["US", "TR"], "hash-1", FilterAction.Exclude).BuildAsync(dbPath, TestCt);
        Assert.AreEqual(0, CountRows(dbPath, "include_v4"));
        Assert.AreEqual(3, CountRows(dbPath, "exclude_v4"));
        Assert.AreEqual("hash-1|Exclude|TR,US", ReadMeta(dbPath, "source_signature"));
    }

    [TestMethod]
    public async Task Country_build_skips_unknown_codes()
    {
        var zipBytes = CreateIpsZip(new Dictionary<string, IpRange[]> {
            ["US"] = [IpRange.Parse("1.0.0.0 - 1.0.0.255")]
        });

        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "split-country-unknown.db");
        await CreateCountryBuilder(zipBytes, ["US", "XX"], "hash-1").BuildAsync(dbPath, TestCt);

        // XX has no .ips entry in the asset → contributes no rows, but the build still succeeds
        Assert.AreEqual(1, CountRows(dbPath, "include_v4"));
        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("hash-1|Include|US,XX", ReadMeta(dbPath, "source_signature"));
    }

    [TestMethod]
    public async Task Country_ensure_reuses_when_unchanged_and_rebuilds_on_change()
    {
        var zipBytes = CreateIpsZip(new Dictionary<string, IpRange[]> {
            ["US"] = [IpRange.Parse("1.0.0.0 - 1.0.0.255")],
            ["TR"] = [IpRange.Parse("5.0.0.0 - 5.0.0.255")]
        });

        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "split-country.db");
        var ct = TestCt;

        await CreateCountryBuilder(zipBytes, ["US", "TR"], "hash-1").EnsureAsync(dbPath, ct);

        // sentinel survives only if the db is NOT rebuilt
        WriteMeta(dbPath, "sentinel", "keep");

        // same asset + same selection (order/case-insensitive) → reuse, sentinel stays
        await CreateCountryBuilder(zipBytes, ["tr", "us"], "hash-1").EnsureAsync(dbPath, ct);
        Assert.AreEqual("keep", ReadMeta(dbPath, "sentinel"), "must reuse when nothing changed");

        // target set changed (mode flip with the same codes) → rebuild, sentinel gone
        await CreateCountryBuilder(zipBytes, ["US", "TR"], "hash-1", FilterAction.Exclude).EnsureAsync(dbPath, ct);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when the target set changes");
        Assert.AreEqual(2, CountRows(dbPath, "exclude_v4"));

        // asset hash changed → rebuild, sentinel gone
        WriteMeta(dbPath, "sentinel", "keep");
        await CreateCountryBuilder(zipBytes, ["US", "TR"], "hash-2").EnsureAsync(dbPath, ct);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when asset hash changes");

        // selection changed → rebuild, sentinel gone
        WriteMeta(dbPath, "sentinel", "keep");
        await CreateCountryBuilder(zipBytes, ["US"], "hash-2").EnsureAsync(dbPath, ct);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when selection changes");
        Assert.AreEqual("hash-2|Include|US", ReadMeta(dbPath, "source_signature"));
    }

    [TestMethod]
    public async Task RangeList_build_creates_ranges_and_meta()
    {
        // disjoint, non-adjacent ranges so ToOrderedList() does not merge them (predictable counts)
        var includes = new[] {
            IpRange.Parse("1.0.0.0 - 1.0.0.255"),
            IpRange.Parse("3.0.0.0 - 3.0.0.255"),
            IpRange.Parse("2001:db8:: - 2001:db8::ffff")
        }.ToOrderedList();

        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "range-list.db");
        await new IpRangeListDbBuilder(() => "sig-1", includesFactory: () => includes).BuildAsync(dbPath, TestCt);

        Assert.IsTrue(File.Exists(dbPath));
        Assert.AreEqual(2, CountRows(dbPath, "include_v4"));
        Assert.AreEqual(1, CountRows(dbPath, "include_v6"));
        Assert.AreEqual(0, CountRows(dbPath, "exclude_v4"));
        Assert.AreEqual(0, CountRows(dbPath, "block_v4"));
        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("sig-1", ReadMeta(dbPath, "source_signature"));

        // the stored set answers membership like any other split-ip db
        using var filter = new SqliteIpFilter(next: null, dbPath);
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("3.0.0.128")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2001:db8::10")));
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("2.0.0.1")));
    }

    [TestMethod]
    public async Task RangeList_ensure_reuses_by_signature_and_parses_only_on_rebuild()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "range-list.db");
        var signature = "sig-1";
        var rangesFactoryCalls = 0;

        IpRangeOrderedList RangesFactory()
        {
            rangesFactoryCalls++;
            return new[] { IpRange.Parse("1.0.0.0 - 1.0.0.255") }.ToOrderedList();
        }

        // ReSharper disable once AccessToModifiedClosure
        var dbBuilder = new IpRangeListDbBuilder(() => signature, includesFactory: RangesFactory);

        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.AreEqual(1, rangesFactoryCalls);

        // same signature → reuse; the source must not be parsed at all
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.AreEqual(1, rangesFactoryCalls, "must not invoke the ranges factory when the db is up to date");

        // changed signature → rebuild
        signature = "sig-2";
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        Assert.AreEqual(2, rangesFactoryCalls);
        Assert.AreEqual("sig-2", ReadMeta(dbPath, "source_signature"));
    }

    [TestMethod]
    public async Task Ensure_rebuilds_on_stale_schema_incomplete_build_or_corrupt_db()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "staleness.db");
        var ipRanges = new[] { IpRange.Parse("1.0.0.0 - 1.0.0.255") }.ToOrderedList();
        var dbBuilder = new IpRangeListDbBuilder(() => "sig-1", includesFactory: () => ipRanges);

        // schema version mismatch (older/newer app) → rebuild
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        WriteMeta(dbPath, "schema_version", "2");
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
        Assert.AreEqual(1, CountRows(dbPath, "include_v4"));
    }

    private async Task<string> BuildUsDbAsync(FilterAction action)
    {
        var zipBytes = CreateIpsZip(new Dictionary<string, IpRange[]> {
            ["US"] = [
                IpRange.Parse("1.0.0.0 - 1.0.0.255"),
                IpRange.Parse("2001:db8:: - 2001:db8::ffff")
            ]
        });
        var dbPath = Path.Combine(TestHelper.WorkingPath, "filter", "split-country.db");
        await CreateCountryBuilder(zipBytes, ["US"], "hash-1", action).BuildAsync(dbPath, TestCt);
        return dbPath;
    }

    private static IpEndPointValue Ep(string ip) => new(IPAddress.Parse(ip), 443);

    [TestMethod]
    public async Task Filter_include_set_vetoes_non_members()
    {
        var dbPath = await BuildUsDbAsync(FilterAction.Include);
        using var filter = new SqliteIpFilter(next: null, dbPath);

        // member ⇒ Default (no objection ⇒ tunnel), including range boundaries; Include is never returned
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.0")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.255")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2001:db8::5")));

        // non-member ⇒ Exclude (bypass/split)
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("1.0.1.0")));
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("2002::1")));
    }

    [TestMethod]
    public async Task Filter_exclude_set_bypasses_members()
    {
        var dbPath = await BuildUsDbAsync(FilterAction.Exclude);
        using var filter = new SqliteIpFilter(next: null, dbPath);

        // member ⇒ Exclude (bypass), non-member ⇒ Default (tunnel)
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("2001:db8::1")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2002::1")));
    }

    [TestMethod]
    public async Task Filter_block_set_drops_members()
    {
        var dbPath = await BuildUsDbAsync(FilterAction.Block);
        using var filter = new SqliteIpFilter(next: null, dbPath);

        // member ⇒ Block (drop), non-member ⇒ Default (tunnel)
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("2001:db8::1")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2002::1")));
    }

    [TestMethod]
    public async Task Filter_empty_db_is_a_no_op_gate()
    {
        var dbPath = Path.Combine(TestHelper.WorkingPath, "filter", "empty.db");
        await new IpRangeListDbBuilder(() => "sig-1").BuildAsync(dbPath, TestCt);
        using var filter = new SqliteIpFilter(next: null, dbPath);

        // all sets empty ⇒ no constraint: always Default (the db is never consulted on the hot path)
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2001:db8::1")));
    }

    [TestMethod]
    public async Task Filter_set_precedence_is_block_exclude_include()
    {
        // one db with all three sets populated; within a db: block > exclude > include-veto
        var dbPath = Path.Combine(TestHelper.WorkingPath, "filter", "precedence.db");
        await new IpRangeListDbBuilder(() => "sig-1",
            includesFactory: () => new[] { IpRange.Parse("1.0.0.0 - 1.0.0.255") }.ToOrderedList(),
            excludesFactory: () => new[] { IpRange.Parse("1.0.0.128 - 1.0.0.255") }.ToOrderedList(),
            blocksFactory: () => new[] { IpRange.Parse("1.0.0.200") }.ToOrderedList()
        ).BuildAsync(dbPath, TestCt);
        using var filter = new SqliteIpFilter(next: null, dbPath);

        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")), "included, not excluded");
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("1.0.0.150")), "exclude wins over include");
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("1.0.0.200")), "block wins over exclude");
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")), "include set vetoes non-members");
    }

    [TestMethod]
    public async Task Filter_pipe_dispose_releases_db_file()
    {
        // Regression: the pipe is disposed from its outermost stage only (NetFilter → CachedIpFilter →
        // StaticIpFilter → SqliteIpFilter). A broken link leaks the SQLite connections, keeps the db file
        // locked, and fails the next rebuild with "file is being used by another process".
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "dispose-chain.db");
        await new IpRangeListDbBuilder(() => "sig-1",
            includesFactory: () => new[] { IpRange.Parse("1.0.0.0 - 1.0.0.255") }.ToOrderedList()
        ).BuildAsync(dbPath, TestCt);

        // opting out of ownership must leave the inner filter untouched
        var innerFilter = new SqliteIpFilter(next: null, dbPath);
        new StaticIpFilter(innerFilter, autoDisposeNextFilter: false).Dispose();
        Assert.AreEqual(FilterAction.Default, innerFilter.Process(IpProtocol.Tcp, Ep("1.0.0.1")),
            "inner filter must survive when the wrapper does not own it");

        // the client's pipe shape (outermost first) owns the whole chain by default
        var pipe = new CachedIpFilter(new StaticIpFilter(innerFilter), TimeSpan.FromMinutes(60));
        Assert.AreEqual(FilterAction.Default, pipe.Process(IpProtocol.Tcp, Ep("1.0.0.1")));
        pipe.Dispose();

        // deleting must succeed: dispose released every handle down the chain
        File.Delete(dbPath);
        Assert.IsFalse(File.Exists(dbPath));
    }

    [TestMethod]
    public async Task Filter_defers_to_inner_next_when_non_default()
    {
        var dbPath = await BuildUsDbAsync(FilterAction.Include);
        using var filter = new SqliteIpFilter(new StubFilter(FilterAction.Block), dbPath);

        // inner (next) runs first; its non-Default result wins over the db decision
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("1.0.0.0")));
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
    }

    private sealed class StubFilter(FilterAction action) : IIpFilter
    {
        public event EventHandler? Changed { add { } remove { } } // verdicts never change
        public bool IsEmpty => false; // always has a verdict
        public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint) => action;
        public void Reconfigure() { } // nothing external to re-read
        public void Dispose() { }
    }

    [TestMethod]
    public async Task SplitIpViaApp_service_builds_db_and_detects_changes()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, "split-ip-via-app-service");
        Directory.CreateDirectory(storagePath);
        var settingsService = new AppSettingsService(storagePath, remoteSettingsUrl: null, debugMode: true);
        var service = new SplitIpViaAppService(settingsService);

        // the UseSplitIpViaApp gate lives in the caller; empty/missing sources leave every set empty,
        // which is a no-op gate (routes identically to no filter)
        var dbPath = await service.EnsureSplitIpDb(storagePath, TestCt);
        StringAssert.Contains(Path.GetFileName(dbPath), "split-ip-via-app.",
            "the file name must carry the context and its source signature");
        using (var filter = new SqliteIpFilter(next: null, dbPath))
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));

        // unchanged sources → same signature → SAME file (reused, not rebuilt)
        Assert.AreEqual(dbPath, await service.EnsureSplitIpDb(storagePath, TestCt),
            "an unchanged source must keep the same versioned file name");

        // the sets mirror the source files: includes veto non-members, excludes bypass, blocks drop.
        // A changed source gets a NEW file name, so a running service could keep the old db open.
        settingsService.SplitIpSettings.AppIncludes = "1.0.0.0 - 1.0.0.255";
        settingsService.SplitIpSettings.AppExcludes = "1.0.0.128 - 1.0.0.255";
        settingsService.SplitIpSettings.AppBlocks = "5.0.0.5";
        var dbPath2 = await service.EnsureSplitIpDb(storagePath, TestCt);
        Assert.AreNotEqual(dbPath, dbPath2, "a changed source must build under a new versioned file name");

        using (var filter = new SqliteIpFilter(next: null, dbPath2)) {
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
            Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("1.0.0.200")),
                "the exclude set must win over the include set");
            Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")),
                "a non-empty include set must veto non-members");
            Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("5.0.0.5")));
            Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("5.0.0.6")),
                "a blocks neighbor is still vetoed by the include set");
        }

        // source file change → signature change → rebuild with the new sets
        settingsService.SplitIpSettings.AppExcludes = string.Empty;
        var dbPath3 = await service.EnsureSplitIpDb(storagePath, TestCt);
        using (var filter = new SqliteIpFilter(next: null, dbPath3))
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.200")));
    }

    [TestMethod]
    public void Selection_inverts_to_smaller_set()
    {
        string[] available = ["US", "TR", "DE", "FR", "IR"];

        // "everything except one" stores the one and flips the target set
        var (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(
            available, ["US", "TR", "DE", "FR"], FilterAction.Include);
        CollectionAssert.AreEquivalent(new[] { "IR" }, codes);
        Assert.AreEqual(FilterAction.Exclude, action);

        // same for the exclude direction
        (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(
            available, ["US", "TR", "DE", "FR"], FilterAction.Exclude);
        CollectionAssert.AreEquivalent(new[] { "IR" }, codes);
        Assert.AreEqual(FilterAction.Include, action);

        // small selection stays as-is (ExcludeMyCountry case)
        (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(available, ["ir"], FilterAction.Exclude);
        CollectionAssert.AreEquivalent(new[] { "IR" }, codes);
        Assert.AreEqual(FilterAction.Exclude, action);

        // tie (complement == selected) must not invert: deterministic and stable
        (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(
            ["US", "TR"], ["us"], FilterAction.Include);
        CollectionAssert.AreEquivalent(new[] { "US" }, codes);
        Assert.AreEqual(FilterAction.Include, action);

        // unknown selected codes contribute nothing and must not skew the size comparison
        (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(
            available, ["US", "TR", "DE", "FR", "XX", "YY"], FilterAction.Include);
        CollectionAssert.AreEquivalent(new[] { "IR" }, codes);
        Assert.AreEqual(FilterAction.Exclude, action);

        // all selected + include => empty exclude set stored (nothing excluded => tunnel everything)
        (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(available, available, FilterAction.Include);
        Assert.IsEmpty(codes);
        Assert.AreEqual(FilterAction.Exclude, action);

        // all selected + exclude must NOT flip: an empty include set means "no constraint", the opposite
        // of "exclude every known country" — store the full exclude set instead
        (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(available, available, FilterAction.Exclude);
        CollectionAssert.AreEquivalent(available, codes);
        Assert.AreEqual(FilterAction.Exclude, action);
    }
}
