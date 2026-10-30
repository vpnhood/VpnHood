using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using VpnHood.Test.KestrelParity.Probes;
using VpnHood.Test.KestrelParity.Probing;
using VpnHood.Test.KestrelParity.Servers;
using VpnHood.Core.Tunneling.ProbeResistance;
using VpnHood.Core.Tunneling.Utils;

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

    // This fixture represents the restricted-network profile: minimum protocol 15. Legacy clients
    // remain supported by a server whose administrator has not raised that minimum yet.
    private static readonly IReadOnlyDictionary<string, KnownDifference> KnownDifferences =
        new Dictionary<string, KnownDifference>();

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
            _ourServer.FarmSecret, testContext.CancellationToken);
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
    public async Task A_signed_proof_passes_both_validators_and_its_expired_twin_is_challenged()
    {
        var ours = _ourServer ?? throw new InvalidOperationException("Our server did not start.");
        var reference = _referenceServer ?? throw new InvalidOperationException("The reference did not start.");
        var proof = new RequestProof(ours.FarmSecret);
        var now = DateTimeOffset.UtcNow;
        var valid = proof.Create(ours.HostName, 15, now);
        var expired = proof.Create(ours.HostName, 15, now.AddHours(-1));

        // The two applications have different authenticated behavior. These positive controls prove
        // both actually accept this signing key, so the expired cases cannot pass on bad signatures.
        Assert.AreEqual(HttpStatusCode.OK,
            await ReadProofStatus(reference.TlsEndPoint, valid, upgrade: false, TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.SwitchingProtocols,
            await ReadProofStatus(ours.TlsEndPoint, valid, upgrade: true, TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            await ReadProofStatus(reference.TlsEndPoint, expired, upgrade: false, TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            await ReadProofStatus(ours.TlsEndPoint, expired, upgrade: true, TestContext.CancellationToken));
    }

    private static async Task<HttpStatusCode> ReadProofStatus(IPEndPoint endPoint, string proof, bool upgrade,
        CancellationToken cancellationToken)
    {
        var host = _ourServer!.HostName;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(endPoint, cancellationToken);
        await using var tls = new SslStream(tcp.GetStream());
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = host,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, cancellationToken);
        var upgradeHeaders = upgrade
            ? "Connection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n"
            : "Connection: close\r\n";
        var request = $"GET /api/status HTTP/1.1\r\nHost: {host}\r\nX-ProtocolVersion: 15\r\n" +
                      $"Authorization: Bearer {proof}\r\n" + upgradeHeaders + "\r\n";
        await tls.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken);
        using var response = await HttpUtils.ReadResponse(tls, cancellationToken);
        return response.StatusCode;
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
        var proof = new RequestProof(ourServer.FarmSecret);
        var results = await ProbeRunner.Run(probes,
            new ProbeTarget("ref", referenceServer.TlsEndPoint, referenceServer.HttpEndPoint, ourServer.HostName, proof),
            new ProbeTarget("ours", ourServer.TlsEndPoint, ourServer.HttpEndPoint, ourServer.HostName, proof),
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
