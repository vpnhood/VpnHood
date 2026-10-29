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
    private const string TlsStart = "ours refuses a request line that starts with 16 03 at once; Kestrel waits for " +
                                    "its line end, then answers 408";
    private const string ZeroByte = "Kestrel refuses a request line at once when a zero byte comes before its line " +
                                    "end; ours waits for the line end";
    private const string Http2Preface = "on the plain port Kestrel answers HTTP/2's preface, as a connection's first " +
                                        "bytes, with a GOAWAY; ours with 505";

    // probes whose answers differ today, each with why; one that comes to match leaves the list
    private static readonly IReadOnlyDictionary<string, string> KnownDifferences = CreateKnownDifferences();

    private static Dictionary<string, string> CreateKnownDifferences()
    {
        var known = new Dictionary<string, string> {
            ["vh-upgrade-v14"] = UntilProtocol15,
            ["vh-post-v14-fake-session"] = UntilProtocol15,
            ["vh-post-v14-no-body"] = UntilProtocol15
        };

        string[] tlsStart = [
            "record-prefix-2", "record-prefix-3", "tls-start", "tls-start-after-blank-line", "tls-start-second-request",
            "tls-start-split", "tls-start-after-request", "tls-start-after-blank-line-packet", "tls-start-third-byte-later"
        ];
        string[] zeroByte = [
            "record-version-2", "record-application-data", "rdp", "smb", "zero-alone", "zero-three", "zero-after-g",
            "zero-in-method", "zero-in-target", "zero-after-version", "zero-after-blank-line", "zero-second-request"
        ];
        foreach (var name in tlsStart)
            AddBothPorts(known, name, TlsStart);
        foreach (var name in zeroByte)
            AddBothPorts(known, name, ZeroByte);
        foreach (var name in new[] {
                     "h2-preface", "h2-preface-settings", "h2-preface-no-final-crlf", "h2-preface-wrong-magic",
                     "h2-request-line", "h2-request-line-blank", "h2-request-line-host"
                 })
            known.Add($"{name}-http", Http2Preface);
        return known;
    }

    private static void AddBothPorts(Dictionary<string, string> known, string name, string why)
    {
        known.Add($"{name}-tls", why);
        known.Add($"{name}-http", why);
    }

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

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_referenceServer != null)
            await _referenceServer.DisposeAsync();
        if (_ourServer != null)
            await _ourServer.DisposeAsync();
        _certificate?.Dispose();
        Directory.Delete(StoragePath, recursive: true);
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

        var unexpected = results.Where(x => !x.IsSame && !KnownDifferences.ContainsKey(x.Name)).Select(x => x.Name).ToArray();
        var matching = results.Where(x => x.IsSame && KnownDifferences.ContainsKey(x.Name)).Select(x => x.Name).ToArray();
        Assert.IsEmpty(unexpected, $"Answered otherwise than Kestrel: {string.Join(", ", unexpected)}. See {reportPath}.");
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

            var known = KnownDifferences.TryGetValue(result.Name, out var why) ? $" (known: {why})" : "";
            report.AppendLine($"DIFF  {result.Name}{known}");
            report.AppendLine("  ref:");
            report.Append(Indent(result.Reference, "    "));
            report.AppendLine("  ours:");
            report.Append(Indent(result.Ours, "    "));
        }

        report.AppendLine($"{results.Count(x => !x.IsSame)} of {results.Count} differ");
        return report.ToString();
    }

    private static string Indent(string text, string prefix)
    {
        return string.Concat(text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => prefix + x.TrimEnd('\r') + "\n"));
    }
}
