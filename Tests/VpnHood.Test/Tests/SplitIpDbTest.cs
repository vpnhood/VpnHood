using System.IO.Compression;
using System.Net;
using Microsoft.Data.Sqlite;
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
    public async Task Build_creates_ranges_and_meta()
    {
        // disjoint, non-adjacent ranges so ToOrderedList() does not merge them (predictable counts)
        var zipBytes = CreateIpsZip(new Dictionary<string, IpRange[]> {
            ["US"] = [IpRange.Parse("1.0.0.0 - 1.0.0.255"), IpRange.Parse("3.0.0.0 - 3.0.0.255")],
            ["TR"] = [IpRange.Parse("5.0.0.0 - 5.0.0.255"), IpRange.Parse("2001:db8:: - 2001:db8::ffff")]
        });

        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "split-country.db");
        await SplitIpDbBuilder.BuildAsync(dbPath, () => new ZipArchive(new MemoryStream(zipBytes)),
            ["US", "TR"], "hash-1", TestCt);

        Assert.IsTrue(File.Exists(dbPath));
        Assert.AreEqual(3, CountRows(dbPath, "range_v4"), "expected 3 IPv4 ranges (US:2 + TR:1)");
        Assert.AreEqual(1, CountRows(dbPath, "range_v6"), "expected 1 IPv6 range");

        Assert.AreEqual("1", ReadMeta(dbPath, "built_complete"));
        Assert.AreEqual("hash-1", ReadMeta(dbPath, "asset_hash"));
        Assert.AreEqual("TR,US", ReadMeta(dbPath, "selection_signature"), "signature must be sorted+upper");
        Assert.AreEqual("1", ReadMeta(dbPath, "schema_version"));
    }

    [TestMethod]
    public async Task Ensure_reuses_when_unchanged_and_rebuilds_on_change()
    {
        var zipBytes = CreateIpsZip(new Dictionary<string, IpRange[]> {
            ["US"] = [IpRange.Parse("1.0.0.0 - 1.0.0.255")],
            ["TR"] = [IpRange.Parse("5.0.0.0 - 5.0.0.255")]
        });

        var dbPath = Path.Combine(TestHelper.WorkingPath, "split-ip-db", "split-country.db");
        var ct = TestCt;

        await SplitIpDbManager.EnsureAsync(dbPath, ZipFactory, ["US", "TR"], "hash-1", ct);

        // sentinel survives only if the db is NOT rebuilt
        WriteMeta(dbPath, "sentinel", "keep");

        // same asset + same selection (order/case-insensitive) → reuse, sentinel stays
        await SplitIpDbManager.EnsureAsync(dbPath, ZipFactory, ["tr", "us"], "hash-1", ct);
        Assert.AreEqual("keep", ReadMeta(dbPath, "sentinel"), "must reuse when nothing changed");

        // asset hash changed → rebuild, sentinel gone
        await SplitIpDbManager.EnsureAsync(dbPath, ZipFactory, ["US", "TR"], "hash-2", ct);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when asset hash changes");

        // selection changed → rebuild, sentinel gone
        WriteMeta(dbPath, "sentinel", "keep");
        await SplitIpDbManager.EnsureAsync(dbPath, ZipFactory, ["US"], "hash-2", ct);
        Assert.IsNull(ReadMeta(dbPath, "sentinel"), "must rebuild when selection changes");
        Assert.AreEqual("US", ReadMeta(dbPath, "selection_signature"));
        return;

        ZipArchive ZipFactory() => new(new MemoryStream(zipBytes));
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
        await SplitIpDbBuilder.BuildAsync(dbPath, () => new ZipArchive(new MemoryStream(zipBytes)),
            ["US"], "hash-1", TestCt);
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
}
