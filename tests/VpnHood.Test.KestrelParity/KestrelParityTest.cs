using System.Security.Cryptography.X509Certificates;
using System.Text;
using VpnHood.Test.KestrelParity.Probes;
using VpnHood.Test.KestrelParity.Probing;
using VpnHood.Test.KestrelParity.Servers;

namespace VpnHood.Test.KestrelParity;

// What our server answers anyone who is not a client, probe by probe against what Kestrel answers, so a later change
// can't drift from it unnoticed. Both servers run in this process with one certificate; ours takes port 80 for its
// HTTP-01 handler. `dotnet test tests/VpnHood.Test.KestrelParity` runs every probe; VH_PARITY_PROBES, a list of
// name prefixes separated by commas, runs only those. Each kind's full report is written where the output says.
[TestClass]
public class KestrelParityTest
{
    private static readonly string RootPath = Path.Combine(Path.GetTempPath(), "vh", "VpnHood.Test.KestrelParity");
    private static readonly string StoragePath = Path.Combine(RootPath, Guid.CreateVersion7().ToString());
    private static X509Certificate2? _certificate;
    private static OurServer? _ourServer;
    private static ReferenceServer? _referenceServer;

    // before protocol 15 a request with our headers carries no key: an upgrade gets its 101, a made-up session our
    // error, and a POST waits for the body that holds the key
    private const string UntilProtocol15 = "a v14 request with our headers is taken as a client's before any key";

    // probes whose answers differ today, each with why and what ours answers; one that comes to match leaves the list
    private static readonly IReadOnlyDictionary<string, KnownDifference> KnownDifferences =
        new Dictionary<string, KnownDifference> {
            ["vh-upgrade-v14"] = new(UntilProtocol15, @"HTTP/1\.1 101 Switching Protocols.*\n= open"),
            ["vh-post-v14-fake-session"] = new(UntilProtocol15,
                @"HTTP/1\.1 200 OK.*""ErrorCode"":""AccessError"".*\n= closed after 0s"),
            ["vh-post-v14-no-body"] = new(UntilProtocol15, @"HTTP/1\.1 401 Unauthorized.*\n= closed after (29|30|31)s")
        };

    public required TestContext TestContext { get; set; }

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext testContext)
    {
        // hundreds of probe connections and both servers share this process's pool
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.SetMinThreads(Math.Max(workerThreads, 4 * Environment.ProcessorCount),
            Math.Max(completionPortThreads, 4 * Environment.ProcessorCount));

        Directory.CreateDirectory(StoragePath);
        _ourServer = await OurServer.Start(StoragePath, testContext.CancellationToken);
        _certificate = X509CertificateLoader.LoadPkcs12FromFile(_ourServer.CertificatePath, null);
        _referenceServer = await ReferenceServer.Start(_certificate, _ourServer.TlsEndPoint.Port,
            testContext.CancellationToken);
    }

    // each step runs whatever the one before it did, so a reference that won't stop can't keep our server on port 80
    // or leave the storage folder; their failures are thrown together
    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        var errors = new List<Exception>();
        foreach (var server in new IAsyncDisposable?[] { _referenceServer, _ourServer }) {
            try {
                if (server != null)
                    await server.DisposeAsync();
            }
            catch (Exception ex) {
                errors.Add(ex);
            }
        }

        _certificate?.Dispose();
        try {
            Directory.Delete(StoragePath, recursive: true);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        if (errors.Count > 0)
            throw new AggregateException(errors);
    }

    [TestMethod]
    public void Probe_names_are_unique()
    {
        var duplicates = ParityProbes.All.GroupBy(x => x.Name).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
        Assert.IsEmpty(duplicates, string.Join(", ", duplicates));
        var unknown = KnownDifferences.Keys.Where(x => ParityProbes.All.All(probe => probe.Name != x)).ToArray();
        Assert.IsEmpty(unknown, $"Known differences no probe has: {string.Join(", ", unknown)}");
    }

    [TestMethod]
    [DataRow(ProbeKind.Quick)]
    [DataRow(ProbeKind.Slow)]
    [DataRow(ProbeKind.Bulk)]
    public async Task Answers_match_kestrel(ProbeKind kind)
    {
        var ourServer = _ourServer ?? throw new InvalidOperationException("Our server did not start.");
        var referenceServer = _referenceServer ?? throw new InvalidOperationException("The reference did not start.");
        var probes = ParityProbes.All.Where(x => x.Kind == kind && IsSelected(x.Name)).ToArray();
        if (probes.Length == 0)
            Assert.Inconclusive("VH_PARITY_PROBES selects no probe of this kind.");

        var reportPath = Path.Combine(RootPath, $"{kind}.txt");
        File.Delete(reportPath + ".progress");
        var results = await ProbeRunner.Run(probes,
            new ProbeTarget("ref", referenceServer.TlsEndPoint, referenceServer.HttpEndPoint, ourServer.HostName),
            new ProbeTarget("ours", ourServer.TlsEndPoint, ourServer.HttpEndPoint, ourServer.HostName),
            reportPath + ".progress", TestContext.CancellationToken);
        await File.WriteAllTextAsync(reportPath, Report(results), TestContext.CancellationToken);
        TestContext.WriteLine($"Kestrel {ReferenceServer.Version}; report: {reportPath}");

        var failed = results.Where(x => x.Failed).Select(x => x.Name).ToArray();
        var unexpected = results.Where(x => !x.IsSame && !KnownDifferences.ContainsKey(x.Name)).Select(x => x.Name).ToArray();
        var drifted = results.Where(x => !x.IsSame && KnownDifferences.TryGetValue(x.Name, out var known) &&
                                         !known.OursMatches(x.Ours)).Select(x => x.Name).ToArray();
        var matching = results.Where(x => x.IsSame && KnownDifferences.ContainsKey(x.Name)).Select(x => x.Name).ToArray();
        Assert.IsEmpty(failed, $"Probes that failed to run, so measured nothing: {string.Join(", ", failed)}. See {reportPath}.");
        Assert.IsEmpty(unexpected, $"Answered otherwise than Kestrel: {string.Join(", ", unexpected)}. See {reportPath}.");
        Assert.IsEmpty(drifted, $"Known differences where ours answers otherwise than listed: {string.Join(", ", drifted)}. " +
                                $"See {reportPath}.");
        Assert.IsEmpty(matching, $"Known differences that now match, to leave the list: {string.Join(", ", matching)}.");
    }

    private static bool IsSelected(string name)
    {
        var prefixes = Environment.GetEnvironmentVariable("VH_PARITY_PROBES");
        return string.IsNullOrWhiteSpace(prefixes) ||
               prefixes.Split(',', StringSplitOptions.TrimEntries).Any(x => name.StartsWith(x, StringComparison.Ordinal));
    }

    private static string Report(IReadOnlyList<ProbeResult> results)
    {
        var report = new StringBuilder();
        foreach (var result in results) {
            if (result.IsSame) {
                report.AppendLine($"SAME  {result.Name}");
                report.Append(Indent(result.Reference, "      "));
                continue;
            }

            report.AppendLine($"DIFF  {result.Name}{KnownNote(result)}");
            report.AppendLine("  ref:");
            report.Append(Indent(result.Reference, "    "));
            report.AppendLine("  ours:");
            report.Append(Indent(result.Ours, "    "));
        }

        report.AppendLine($"{results.Count(x => !x.IsSame)} of {results.Count} differ");
        return report.ToString();
    }

    private static string KnownNote(ProbeResult result)
    {
        if (!KnownDifferences.TryGetValue(result.Name, out var known))
            return "";

        return known.OursMatches(result.Ours)
            ? $" (known: {known.Why})"
            : $" (known, but ours no longer answers as listed, {known.Ours}: {known.Why})";
    }

    private static string Indent(string text, string prefix)
    {
        return string.Concat(text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => prefix + x.TrimEnd('\r') + "\n"));
    }
}
