using System.Net;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Tests;

// The self-updating split filter stages: Reconfigure re-reads the folder's manifest and swaps the db
// gates only when the paths changed — the permanent inner filter is never recreated, in-flight lookups
// drain on the old gates before disposal, the superseded db files are deleted by the stage itself (it
// held them open), and the Changed event rolls up so the caches above invalidate themselves. This is
// what lets the host live-apply split changes with zero wiring.
[TestClass]
public class SqliteFilterChainTest : TestBase
{
    private static IpEndPointValue Ep(string ip) => new(IPAddress.Parse(ip), 443);

    private string CreateDbFolder(string name)
    {
        var folder = Path.Combine(TestHelper.WorkingPath, "sqlite-filter-chain", name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class StubIpFilter : IIpFilter
    {
        public int ReconfigureCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public event EventHandler? Changed { add { } remove { } } // verdicts never change
        public bool IsEmpty => true; // never decides (always Default)
        public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint) => FilterAction.Default;
        public void Reconfigure() => ReconfigureCount++;
        public void Dispose() => IsDisposed = true;
    }

    private sealed class StubDomainFilter : IDomainFilter
    {
        public int ReconfigureCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public event EventHandler? Changed { add { } remove { } } // verdicts never change
        public bool IsEmpty => true; // never decides (always Default)
        public FilterAction Process(string? domain) => FilterAction.Default;
        public void Reconfigure() => ReconfigureCount++;
        public void Dispose() => IsDisposed = true;
    }

    private async Task<string> BuildIpDb(string folder, string fileName, string blockIp)
    {
        var dbPath = Path.Combine(folder, fileName);
        await new IpRangeListDbBuilder(() => fileName,
            blocksFactory: () => new[] { IpRange.Parse(blockIp) }.ToOrderedList()).BuildAsync(dbPath, TestCt);
        return dbPath;
    }

    private async Task<string> BuildDomainDb(string folder, string fileName, string blockDomain)
    {
        var dbPath = Path.Combine(folder, fileName);
        await new DomainListDbBuilder(() => fileName, blocksFactory: () => [blockDomain]).BuildAsync(dbPath, TestCt);
        return dbPath;
    }

    [TestMethod]
    public async Task SqliteIpFilterChain_reconfigure_swaps_gates_and_deletes_superseded_db()
    {
        var folder = CreateDbFolder("ip-swap");
        var oldDbPath = await BuildIpDb(folder, "ip-swap.1.db", "1.0.0.1");
        SplitDbManifest.Write(folder, [oldDbPath]);

        var inner = new StubIpFilter();
        using var chain = new SqliteIpFilterChain(inner, folder);

        Assert.AreEqual(FilterAction.Block, chain.Process(IpProtocol.Tcp, Ep("1.0.0.1")));
        Assert.AreEqual(0, inner.ReconfigureCount, "construction is not a change: no command, no event");

        // the real versioned flow: a new file appears, the manifest flips to it (its sweep cannot
        // delete the old db — this chain still holds it open), then the reconfigure signal arrives
        var newDbPath = await BuildIpDb(folder, "ip-swap.2.db", "2.0.0.2");
        SplitDbManifest.Write(folder, [newDbPath]);
        chain.Reconfigure();

        Assert.AreEqual(FilterAction.Default, chain.Process(IpProtocol.Tcp, Ep("1.0.0.1")));
        Assert.AreEqual(FilterAction.Block, chain.Process(IpProtocol.Tcp, Ep("2.0.0.2")));
        Assert.IsFalse(File.Exists(oldDbPath), "the stage must delete the superseded db it had open");
        Assert.IsTrue(File.Exists(newDbPath));
        Assert.IsFalse(inner.IsDisposed, "the permanent inner filter must survive the swap");
        Assert.AreEqual(1, inner.ReconfigureCount, "the command must roll down past the gates");
    }

    [TestMethod]
    public async Task SqliteIpFilterChain_reconfigure_noops_when_manifest_unchanged()
    {
        var folder = CreateDbFolder("ip-noop");
        var dbPath = await BuildIpDb(folder, "ip-noop.1.db", "1.0.0.1");
        SplitDbManifest.Write(folder, [dbPath]);
        using var chain = new SqliteIpFilterChain(next: null, folder);

        var changedCount = 0;
        chain.Changed += (_, _) => changedCount++;

        chain.Reconfigure();
        Assert.AreEqual(0, changedCount, "unchanged paths must not swap (no cache invalidation storm)");
        Assert.IsTrue(File.Exists(dbPath));
        Assert.AreEqual(FilterAction.Block, chain.Process(IpProtocol.Tcp, Ep("1.0.0.1")));
    }

    [TestMethod]
    public async Task SqliteDomainFilterChain_reconfigure_swaps_gates_and_deletes_superseded_db()
    {
        var folder = CreateDbFolder("domain-swap");
        var oldDbPath = await BuildDomainDb(folder, "domain-swap.1.db", "old.com");
        SplitDbManifest.Write(folder, [oldDbPath]);

        var inner = new StubDomainFilter();
        using var chain = new SqliteDomainFilterChain(inner, folder);

        Assert.AreEqual(FilterAction.Block, chain.Process("old.com"));

        var newDbPath = await BuildDomainDb(folder, "domain-swap.2.db", "new.com");
        SplitDbManifest.Write(folder, [newDbPath]);
        chain.Reconfigure();

        Assert.AreEqual(FilterAction.Default, chain.Process("old.com"));
        Assert.AreEqual(FilterAction.Block, chain.Process("new.com"));
        Assert.IsFalse(File.Exists(oldDbPath), "the stage must delete the superseded db it had open");
        Assert.IsFalse(inner.IsDisposed, "the permanent inner filter must survive the swap");
        Assert.AreEqual(1, inner.ReconfigureCount, "the command must roll down past the gates");
    }

    [TestMethod]
    public async Task Manifest_publishes_the_db_set_and_sweeps_superseded_files()
    {
        var folder = CreateDbFolder("manifest");

        // a missing manifest means "no splits configured" — never inferred from files lying in the folder
        var strayDbPath = Path.Combine(folder, "stray.db");
        await new DomainListDbBuilder(() => "stray", blocksFactory: () => ["stray.com"]).BuildAsync(strayDbPath, TestCt);
        Assert.AreEqual(0, SplitDbManifest.Read(folder).Length, "presence on disk must not be policy");

        // publish one db: Read resolves it, the unlisted stray gets swept, non-db files survive
        var notesPath = Path.Combine(folder, "notes.txt");
        await File.WriteAllTextAsync(notesPath, "keep me", TestCt);
        var dbPath = Path.Combine(folder, "split-domain.1.db");
        await new DomainListDbBuilder(() => "sig-1", blocksFactory: () => ["a.com"]).BuildAsync(dbPath, TestCt);
        SplitDbManifest.Write(folder, [dbPath]);

        CollectionAssert.AreEqual(new[] { dbPath }, SplitDbManifest.Read(folder));
        Assert.IsFalse(File.Exists(strayDbPath), "an unlisted db-family file is superseded and swept");
        Assert.IsTrue(File.Exists(notesPath), "the sweep must only ever touch db-family files");

        // publishing an empty set is the OFF switch: same flow, empty case
        SplitDbManifest.Write(folder, []);
        Assert.AreEqual(0, SplitDbManifest.Read(folder).Length);
        Assert.IsFalse(File.Exists(dbPath));

        // a db outside the folder must be rejected: the manifest names files, not locations
        Assert.ThrowsExactly<ArgumentException>(() =>
            SplitDbManifest.Write(folder, [Path.Combine(TestHelper.WorkingPath, "elsewhere.db")]));
    }

    [TestMethod]
    public async Task IsEmpty_reflects_the_whole_pipe_and_flips_on_reconfigure()
    {
        // the client derives SNI extraction from IsEmpty and re-checks it on Changed — so emptiness must
        // aggregate the pipe and flip with a gate swap
        var folder = CreateDbFolder("is-empty");
        var emptyDbPath = Path.Combine(folder, "empty.1.db");
        await new DomainListDbBuilder(() => "empty").BuildAsync(emptyDbPath, TestCt);
        SplitDbManifest.Write(folder, [emptyDbPath]);

        var chain = new SqliteDomainFilterChain(next: null, folder);
        using var cached = new CachedDomainFilter(chain, TimeSpan.FromMinutes(60));
        Assert.IsTrue(cached.IsEmpty, "a gate whose sets are all empty holds no rules");

        var changedCount = 0;
        cached.Changed += (_, _) => changedCount++;

        var rulesDbPath = await BuildDomainDb(folder, "rules.1.db", "example.com");
        SplitDbManifest.Write(folder, [emptyDbPath, rulesDbPath]);
        chain.Reconfigure();
        Assert.IsFalse(cached.IsEmpty, "rules appeared: the endpoint re-checking on Changed must see them");
        Assert.AreEqual(1, changedCount, "the swap must announce itself so the endpoint knows to re-check");
    }

    [TestMethod]
    public async Task Cache_above_the_split_filter_self_invalidates_on_reconfigure()
    {
        // the client pipe shape: the cache must drop its verdicts on the rolled-up Changed event,
        // with no external ClearCache call anywhere
        var folder = CreateDbFolder("domain-cache");
        var oldDbPath = await BuildDomainDb(folder, "domain-cache.1.db", "example.com");
        SplitDbManifest.Write(folder, [oldDbPath]);

        var chain = new SqliteDomainFilterChain(next: null, folder);
        using var cached = new CachedDomainFilter(chain, TimeSpan.FromMinutes(60));

        Assert.AreEqual(FilterAction.Block, cached.Process("example.com"));

        var newDbPath = await BuildDomainDb(folder, "domain-cache.2.db", "other.com");
        SplitDbManifest.Write(folder, [newDbPath]);
        chain.Reconfigure();

        Assert.AreEqual(FilterAction.Default, cached.Process("example.com"),
            "the cache must invalidate itself on the rolled-up change event");
        Assert.AreEqual(FilterAction.Block, cached.Process("other.com"));
    }

    [TestMethod]
    public async Task Reconfigure_swaps_safely_under_concurrent_lookups()
    {
        // the drain guarantee: a swap must wait for in-flight lookups to leave the old gates before
        // disposing them (and deleting their files), independent of what the gates are made of. Hammer
        // the stage from several threads while swapping to a freshly copied db over and over — without
        // the drain this reliably surfaces disposed-connection failures.
        var folder = CreateDbFolder("stress");
        var templatePath = Path.Combine(TestHelper.WorkingPath, "sqlite-filter-chain", "stress-template.db");
        await new DomainListDbBuilder(() => "stress", blocksFactory: () => ["stress.com"])
            .BuildAsync(templatePath, TestCt);

        using var filter = new SqliteDomainFilterChain(next: null, folder);

        using var stopCts = new CancellationTokenSource();
        var firstError = (Exception?)null;
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            while (!stopCts.IsCancellationRequested)
                try {
                    filter.Process("stress.com");
                    filter.Process("other.com");
                }
                catch (Exception ex) {
                    firstError ??= ex;
                }
        })).ToArray();

        // each round publishes a NEW file (the real versioned flow); the manifest sweep cannot delete
        // the previous db (the chain holds it open) — the swap disposes and deletes it
        for (var i = 0; i < 40 && firstError == null; i++) {
            var dbPath = Path.Combine(folder, $"stress.{i}.db");
            File.Copy(templatePath, dbPath);
            SplitDbManifest.Write(folder, [dbPath]);
            filter.Reconfigure();
        }

        stopCts.Cancel();
        await Task.WhenAll(workers);
        Assert.IsNull(firstError, $"a lookup raced a swap: {firstError}");
        Assert.AreEqual(FilterAction.Block, filter.Process("stress.com"));
    }
}
