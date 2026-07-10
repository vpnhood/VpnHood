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

    private static SplitCountryDbBuilder CreateCountryBuilder(byte[] zipBytes, string[] countryCodes, string assetHash) =>
        new(() => new ZipArchive(new MemoryStream(zipBytes)), countryCodes, assetHash);

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
        Assert.AreEqual(3, CountRows(dbPath, "range_v4"), "expected 3 IPv4 ranges (US:2 + TR:1)");
        Assert.AreEqual(1, CountRows(dbPath, "range_v6"), "expected 1 IPv6 range");

        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("hash-1|TR,US", ReadMeta(dbPath, "source_signature"), "asset hash + sorted+upper codes");
        Assert.AreEqual("2", ReadMeta(dbPath, "schema_version"));
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
        Assert.AreEqual(1, CountRows(dbPath, "range_v4"));
        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("hash-1|US,XX", ReadMeta(dbPath, "source_signature"));
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

        // asset hash changed → rebuild, sentinel gone
        await CreateCountryBuilder(zipBytes, ["US", "TR"], "hash-2").EnsureAsync(dbPath, ct);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when asset hash changes");

        // selection changed → rebuild, sentinel gone
        WriteMeta(dbPath, "sentinel", "keep");
        await CreateCountryBuilder(zipBytes, ["US"], "hash-2").EnsureAsync(dbPath, ct);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when selection changes");
        Assert.AreEqual("hash-2|US", ReadMeta(dbPath, "source_signature"));
    }

    [TestMethod]
    public async Task RangeList_build_creates_ranges_and_meta()
    {
        // disjoint, non-adjacent ranges so ToOrderedList() does not merge them (predictable counts)
        var ipRanges = new[] {
            IpRange.Parse("1.0.0.0 - 1.0.0.255"),
            IpRange.Parse("3.0.0.0 - 3.0.0.255"),
            IpRange.Parse("2001:db8:: - 2001:db8::ffff")
        }.ToOrderedList();

        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "range-list.db");
        await new IpRangeListDbBuilder(() => ipRanges, () => "sig-1").BuildAsync(dbPath, TestCt);

        Assert.IsTrue(File.Exists(dbPath));
        Assert.AreEqual(2, CountRows(dbPath, "range_v4"));
        Assert.AreEqual(1, CountRows(dbPath, "range_v6"));
        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("sig-1", ReadMeta(dbPath, "source_signature"));

        // the stored set answers membership like any other split-ip db
        using var filter = new SqliteIpFilter(next: null, dbPath, FilterAction.Include);
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

        var dbBuilder = new IpRangeListDbBuilder(RangesFactory, () => signature);

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
        var dbBuilder = new IpRangeListDbBuilder(() => ipRanges, () => "sig-1");

        // schema version mismatch (older/newer app) → rebuild
        await dbBuilder.EnsureAsync(dbPath, TestCt);
        WriteMeta(dbPath, "schema_version", "1");
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
        Assert.AreEqual(1, CountRows(dbPath, "range_v4"));
    }

    private async Task<string> BuildUsDbAsync()
    {
        var zipBytes = CreateIpsZip(new Dictionary<string, IpRange[]> {
            ["US"] = [
                IpRange.Parse("1.0.0.0 - 1.0.0.255"),
                IpRange.Parse("2001:db8:: - 2001:db8::ffff")
            ]
        });
        var dbPath = Path.Combine(TestHelper.WorkingPath, "filter", "split-country.db");
        await CreateCountryBuilder(zipBytes, ["US"], "hash-1").BuildAsync(dbPath, TestCt);
        return dbPath;
    }

    private static IpEndPointValue Ep(string ip) => new(IPAddress.Parse(ip), 443);

    [TestMethod]
    public async Task Filter_include_action_tunnels_only_selected()
    {
        var dbPath = await BuildUsDbAsync();
        using var filter = new SqliteIpFilter(next: null, dbPath, FilterAction.Include);

        // member ⇒ Default (defer/tunnel-eligible), including range boundaries
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.0")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.255")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2001:db8::5")));

        // non-member ⇒ Exclude (bypass/split)
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("1.0.1.0")));
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("2002::1")));
    }

    [TestMethod]
    public async Task Filter_exclude_action_tunnels_everything_else()
    {
        var dbPath = await BuildUsDbAsync();
        using var filter = new SqliteIpFilter(next: null, dbPath, FilterAction.Exclude);

        // member ⇒ Exclude (bypass), non-member ⇒ Default (tunnel)
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("2001:db8::1")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2002::1")));
    }

    [TestMethod]
    public async Task Filter_block_action_drops_selected()
    {
        var dbPath = await BuildUsDbAsync();
        using var filter = new SqliteIpFilter(next: null, dbPath, FilterAction.Block);

        // member ⇒ Block (drop), non-member ⇒ Default (tunnel)
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("2001:db8::1")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("2002::1")));
    }

    [TestMethod]
    public async Task Filter_default_action_skips_db_check()
    {
        var dbPath = await BuildUsDbAsync();
        using var filter = new SqliteIpFilter(next: null, dbPath, FilterAction.Default);

        // Default action ⇒ always Default, member or not (db is never consulted)
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
    }

    [TestMethod]
    public async Task Filter_pipe_dispose_releases_db_file()
    {
        // Regression: the pipe is disposed from its outermost stage only (NetFilter → CachedIpFilter →
        // StaticIpFilter → SqliteIpFilter). A broken link leaks the SQLite connections, keeps the db file
        // locked, and fails the next rebuild with "file is being used by another process".
        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "dispose-chain.db");
        await new IpRangeListDbBuilder(
            () => new[] { IpRange.Parse("1.0.0.0 - 1.0.0.255") }.ToOrderedList(),
            () => "sig-1").BuildAsync(dbPath, TestCt);

        // the client's pipe shape (outermost first), with the db gate innermost
        var pipe = new CachedIpFilter(
            new StaticIpFilter(new SqliteIpFilter(next: null, dbPath, FilterAction.Include)),
            TimeSpan.FromMinutes(60));
        Assert.AreEqual(FilterAction.Default, pipe.Process(IpProtocol.Tcp, Ep("1.0.0.1"))); // opens the connection
        pipe.Dispose();

        // deleting must succeed: dispose released every handle down the chain
        File.Delete(dbPath);
        Assert.IsFalse(File.Exists(dbPath));
    }

    [TestMethod]
    public async Task Filter_defers_to_inner_next_when_non_default()
    {
        var dbPath = await BuildUsDbAsync();
        using var filter = new SqliteIpFilter(new StubFilter(FilterAction.Block), dbPath, FilterAction.Include);

        // inner (next) runs first; its non-Default result wins over the db decision
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("1.0.0.0")));
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
    }

    private sealed class StubFilter(FilterAction action) : IIpFilter
    {
        public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint) => action;
        public void Dispose() { }
    }

    [TestMethod]
    public async Task SplitIpViaApp_service_builds_merged_db_and_detects_changes()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, "split-ip-via-app-service");
        Directory.CreateDirectory(storagePath);
        var settingsService = new AppSettingsService(storagePath, remoteSettingsUrl: null, debugMode: true);
        var service = new SplitIpViaAppService(settingsService);
        var includeDbPath = Path.Combine(storagePath, "split-ip-via-app.db");
        var blockDbPath = Path.Combine(storagePath, "split-ip-via-app-blocks.db");

        // the UseSplitIpViaApp gate lives in the caller; empty/missing sources build no-op dbs
        // (include merges to All, blocks to None), which route identically to no filter
        var filters = await service.EnsureSplitIpDbs(storagePath, TestCt);
        CollectionAssert.AreEqual(new[] { FilterAction.Include, FilterAction.Block },
            filters.Select(x => x.Action).ToArray());
        using (var filter = new SqliteIpFilter(next: null, includeDbPath, FilterAction.Include))
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        using (var filter = new SqliteIpFilter(next: null, blockDbPath, FilterAction.Block))
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));

        // includes − excludes merged into ONE stored set with Include semantics; blocks in their own db
        settingsService.SplitIpSettings.AppIncludes = "1.0.0.0 - 1.0.0.255";
        settingsService.SplitIpSettings.AppExcludes = "1.0.0.128 - 1.0.0.255";
        settingsService.SplitIpSettings.AppBlocks = "5.0.0.5";
        await service.EnsureSplitIpDbs(storagePath, TestCt);

        using (var filter = new SqliteIpFilter(next: null, includeDbPath, FilterAction.Include)) {
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.10")));
            Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("1.0.0.200")),
                "excluded sub-range must not be a member of the merged set");
            Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("9.9.9.9")));
        }

        using (var filter = new SqliteIpFilter(next: null, blockDbPath, FilterAction.Block)) {
            Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("5.0.0.5")));
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("5.0.0.6")));
        }

        // source file change → signature change → rebuild with the new merge
        settingsService.SplitIpSettings.AppExcludes = string.Empty;
        await service.EnsureSplitIpDbs(storagePath, TestCt);
        using (var filter = new SqliteIpFilter(next: null, includeDbPath, FilterAction.Include))
            Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("1.0.0.200")));
    }

    [TestMethod]
    public void Selection_inverts_to_smaller_set()
    {
        string[] available = ["US", "TR", "DE", "FR", "IR"];

        // "everything except one" stores the one and flips the action
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

        // all selected => empty complement stored, flipped action (nothing excluded => tunnel everything)
        (codes, action) = SplitCountryService.ResolveSplitIpDbSelection(available, available, FilterAction.Include);
        Assert.IsEmpty(codes);
        Assert.AreEqual(FilterAction.Exclude, action);
    }
}
