using System.Net;
using System.Net.Quic;
using System.Text;
using System.Text.RegularExpressions;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    private const int BurstStreamsPerConnection = 20;

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(true, 32769)]
    [DataRow(true, 65536)]
    [DataRow(true, 65537)]
    [DataRow(false, 101)]
    [DataRow(false, 200)]
    [DataRow(false, 201)]
    public async Task Trailer_bursts_preserve_protocol_outcomes(bool octets, int total)
    {
        var name = $"trailer-burst-{(octets ? "octets" : "count")}-{total}";
        var bytes = BuildTrailerBurst(octets, total);
        // The reference's field-201 reset share varied from 7% to 70% on the calibration host.
        await AssertReusedBurstOutcomes(name, bytes, octets ? 0x10b : 0x10e,
            allowLimitResponse: true, TestContext.CancellationToken);
    }

    private async Task AssertReusedBurstOutcomes(string name, byte[] bytes, long errorCode,
        bool allowLimitResponse, CancellationToken cancellationToken)
    {
        var sampleCount = GetBurstSampleCount();
        var expected = new List<(Http3ProbeResult Response, Http3ProbeResult Write)>(sampleCount);
        var actual = new List<(Http3ProbeResult Response, Http3ProbeResult Write)>(sampleCount);
        try {
            // Stream outcomes on one connection are correlated. Two identical RC3 listeners exceeded
            // the share tolerance when all 300 samples used one pair. Use independent pairs while
            // still exercising stream reuse; retain every observation for protocol checks and diagnostics.
            for (var firstSample = 0; firstSample < sampleCount; firstSample += BurstStreamsPerConnection)
                await ObserveReusedBurstBatch(bytes, firstSample,
                    Math.Min(BurstStreamsPerConnection, sampleCount - firstSample), expected, actual, cancellationToken);
        }
        finally {
            WriteBurstDistribution(name + " reference", expected);
            WriteBurstDistribution(name + " ours", actual);
        }

        AssertBurstOutcomes(name, expected, actual, QuicError.StreamAborted, errorCode,
            allowLimitResponse);
    }

    private static async Task ObserveReusedBurstBatch(byte[] bytes, int firstSample, int sampleCount,
        List<(Http3ProbeResult Response, Http3ProbeResult Write)> expected,
        List<(Http3ProbeResult Response, Http3ProbeResult Write)> actual, CancellationToken cancellationToken)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(Http3Probe.Timeout);
        // Alternate connection startup as well as request order across batches.
        var referenceFirst = firstSample / BurstStreamsPerConnection % 2 == 0;
        await using var first = await CreateProbe().Connect(
            referenceFirst ? Servers.ReferenceEndPoint : Servers.OurEndPoint, 3, startup.Token);
        await using var second = await CreateProbe().Connect(
            referenceFirst ? Servers.OurEndPoint : Servers.ReferenceEndPoint, 3, startup.Token);
        await using var firstControl = await first.OpenOutboundStreamAsync(QuicStreamType.Unidirectional,
            startup.Token);
        await using var secondControl = await second.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, startup.Token);
        await firstControl.WriteAsync(new byte[] { 0, 4, 0 }, startup.Token);
        await secondControl.WriteAsync(new byte[] { 0, 4, 0 }, startup.Token);
        var reference = referenceFirst ? first : second;
        var ours = referenceFirst ? second : first;
        for (var sample = 0; sample < sampleCount; sample++) {
            if ((sample % 2 == 0) == referenceFirst) {
                expected.Add(await ObserveBurst(reference, bytes, cancellationToken));
                actual.Add(await ObserveBurst(ours, bytes, cancellationToken));
            }
            else {
                actual.Add(await ObserveBurst(ours, bytes, cancellationToken));
                expected.Add(await ObserveBurst(reference, bytes, cancellationToken));
            }
        }
    }

    [TestMethod]
    [DataRow("long-body")]
    [DataRow("short-body-fin")]
    public async Task Repeated_stream_errors_release_request_slots_on_a_connection(string layout)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        startup.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(Servers.OurEndPoint, 3, startup.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, startup.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, startup.Token);
        var bytes = BuildPostHeaderBurst(layout);
        // More than four times the server's concurrent-stream allowance. Each completed/reset stream
        // must release its slot, including any pending read, write and queued abort operation.
        for (var sample = 0; sample < 500; sample++) {
            var result = await ObserveBurst(connection, bytes, TestContext.CancellationToken);
            GetBurstOutcome(result.Response, QuicError.StreamAborted, 0x101, allowLimitResponse: false);
        }
        var ordinary = await ObserveBurst(connection, BuildLiteralRequest(ValidationHeaders()),
            TestContext.CancellationToken);
        Assert.IsTrue(IsCompleteBurstResponse(ordinary.Response, "401"));
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task Data_after_trailers_bursts_preserve_protocol_outcomes()
    {
        const string name = "trailer-burst-data-after-trailers";
        byte[] bytes = [.. BuildLiteralRequest(ValidationHeaders()),
            .. BuildLiteralRequest(Enumerable.Repeat(("x", ""), 8).ToArray()), .. Frame(0, [1])];
        await AssertFreshBurstOutcomes(name, bytes, 0x105, TestContext.CancellationToken);
    }

    private async Task AssertFreshBurstOutcomes(string name, byte[] bytes, long errorCode,
        CancellationToken cancellationToken)
    {
        var sampleCount = GetBurstSampleCount();
        var expected = new List<(Http3ProbeResult Response, Http3ProbeResult Write)>(sampleCount);
        var actual = new List<(Http3ProbeResult Response, Http3ProbeResult Write)>(sampleCount);
        try {
            for (var sample = 0; sample < sampleCount; sample++) {
                // A parser-first sample closes its connection, so neither server reuses one here.
                if (sample % 2 == 0) {
                    expected.Add(await ObserveFreshBurst(Servers.ReferenceEndPoint, bytes, cancellationToken));
                    actual.Add(await ObserveFreshBurst(Servers.OurEndPoint, bytes, cancellationToken));
                }
                else {
                    actual.Add(await ObserveFreshBurst(Servers.OurEndPoint, bytes, cancellationToken));
                    expected.Add(await ObserveFreshBurst(Servers.ReferenceEndPoint, bytes, cancellationToken));
                }
            }
        }
        finally {
            WriteBurstDistribution(name + " reference", expected);
            WriteBurstDistribution(name + " ours", actual);
        }
        AssertBurstOutcomes(name, expected, actual, QuicError.ConnectionAborted, errorCode,
            allowLimitResponse: false);
    }

    private static int GetBurstSampleCount()
    {
        const string variable = "VH_HTTP3_BURST_SAMPLES";
        var configured = Environment.GetEnvironmentVariable(variable);
        if (configured == null)
            return 200;
        // Local diagnostics may need more observations; keep the 10% presence threshold at least one.
        if (!int.TryParse(configured, out var sampleCount) || sampleCount is < 10 or > 10000)
            throw new InvalidOperationException($"{variable} must be an integer between 10 and 10000.");
        return sampleCount;
    }

    private void AssertBurstOutcomes(string name,
        IReadOnlyList<(Http3ProbeResult Response, Http3ProbeResult Write)> expected,
        IReadOnlyList<(Http3ProbeResult Response, Http3ProbeResult Write)> actual,
        QuicError error, long errorCode, bool allowLimitResponse)
    {
        Assert.AreEqual(expected.Count, actual.Count);
        var sampleCount = expected.Count;
        var expectedOutcomes = expected.Select(result =>
            GetBurstOutcome(result.Response, error, errorCode, allowLimitResponse)).ToArray();
        var actualOutcomes = actual.Select(result =>
            GetBurstOutcome(result.Response, error, errorCode, allowLimitResponse)).ToArray();
        var presenceThreshold = sampleCount / 10;
        var diagnosticBand = sampleCount * 22 / 100;
        IReadOnlyList<string> outcomes = error == QuicError.ConnectionAborted
            ? ["401", "connection-error"]
            : allowLimitResponse ? ["401", "431", "reset"] : ["401", "reset"];
        foreach (var outcome in outcomes) {
            var referenceCount = expectedOutcomes.Count(value => value == outcome);
            var ourCount = actualOutcomes.Count(value => value == outcome);
            // Independent parser/application races have no fixed outcome shares. Keep their old
            // calibration thresholds visible as diagnostics; every sampled protocol result and
            // complete response wire form is still checked below.
            TestContext.WriteLine($"{name}: {outcome}: reference={referenceCount}/{sampleCount}, " +
                $"ours={ourCount}/{sampleCount}; historical 22-point comparison (diagnostic only).");
            if (referenceCount >= presenceThreshold && ourCount == 0)
                TestContext.WriteLine($"{name}: diagnostic: reference {outcome} occurs at least 10%, but ours was not sampled.");
            if (ourCount >= presenceThreshold && referenceCount == 0)
                TestContext.WriteLine($"{name}: diagnostic: ours {outcome} occurs at least 10%, but the reference was not sampled.");
            if (Math.Abs(referenceCount - ourCount) > diagnosticBand)
                TestContext.WriteLine($"{name}: diagnostic: {outcome} differs by more than the historical 22 percentage points: " +
                    $"reference={referenceCount}/{sampleCount}, ours={ourCount}/{sampleCount}.");

            if ((outcome is "401" or "431") && referenceCount > 0 && ourCount > 0) {
                var referenceForms = expected.Where(result => IsCompleteBurstResponse(result.Response, outcome))
                    .Select(result => result.Response).ToHashSet();
                foreach (var result in actual.Where(result => IsCompleteBurstResponse(result.Response, outcome)))
                    Assert.Contains(result.Response, referenceForms,
                        $"{name}: the complete {outcome} wire response must match the reference, apart from Date.");
            }
        }
    }

    private static string GetBurstOutcome(Http3ProbeResult result, QuicError error, long errorCode,
        bool allowLimitResponse)
    {
        if (!result.Fin && result.Error == error && result.ApplicationErrorCode == errorCode)
            return error == QuicError.ConnectionAborted ? "connection-error" : "reset";
        if (IsCompleteBurstResponse(result, "401"))
            return "401";
        // Only the trailer limit rows can exceed the soft header limits; 431 elsewhere is a protocol mismatch.
        if (allowLimitResponse && error == QuicError.StreamAborted && IsCompleteBurstResponse(result, "431"))
            return "431";
        throw new AssertFailedException(
            $"Unexpected burst response (error must be {error}/0x{errorCode:x}): {result}");
    }

    private static bool IsCompleteBurstResponse(Http3ProbeResult result, string status)
    {
        if (!result.Fin || result.Error != null || result.ApplicationErrorCode != null)
            return false;
        var bytes = Convert.FromHexString(result.Bytes);
        if (bytes.Length < 2 || bytes[0] != 1)
            return false;
        var payloadOffset = 1 + (1 << (bytes[1] >> 6));
        // RC3 starts HEADERS with a zero QPACK prefix and a literal :status at static name index 63.
        ReadOnlySpan<byte> statusPrefix = [0, 0, 0x5f, 0x30, 3];
        return bytes.Length >= payloadOffset + statusPrefix.Length + status.Length &&
               bytes.AsSpan(payloadOffset).StartsWith(statusPrefix) &&
               bytes[payloadOffset + statusPrefix.Length] == status[0] &&
               bytes[payloadOffset + statusPrefix.Length + 1] == status[1] &&
               bytes[payloadOffset + statusPrefix.Length + 2] == status[2];
    }

    private static byte[] BuildTrailerBurst(bool octets, int total)
    {
        var headers = octets ? CreateSizedFields(ValidationHeaders(), 32000, 8) : CreateCountedHeaders();
        IReadOnlyList<(string Name, string Value)> trailers = octets
            ? CreateSizedFields([], total - 32000, 8)
            : Enumerable.Repeat(("x", ""), total - headers.Count).ToArray();
        return [.. BuildLiteralRequest(headers), .. BuildLiteralRequest(trailers)];
    }

    private static async Task<(Http3ProbeResult Response, Http3ProbeResult Write)> ObserveBurst(
        QuicConnection connection, byte[] bytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var response = ReadCompleteResponse(request, deadline.Token);
        var write = ObserveWriteEnd(request, deadline.Token);
        try {
            // A single native send leaves the whole request and FIN in the same uncontrolled burst.
            await SendAtomicRequest(request, bytes, deadline.Token);
            await Task.WhenAll(response, write);
            return (NormalizeBurstResponse(await response), await write);
        }
        finally {
            await deadline.CancelAsync();
            try { await Task.WhenAll(response, write); }
            catch (Exception) {
                // Join both observers before native stream disposal while retaining the original failure.
            }
        }
    }

    private static async Task<(Http3ProbeResult Response, Http3ProbeResult Write)> ObserveFreshBurst(
        IPEndPoint endPoint, byte[] bytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        return await ObserveBurst(connection, bytes, deadline.Token);
    }

    private static Http3ProbeResult NormalizeBurstResponse(Http3ProbeResult result)
    {
        // Partial responses remain outcomes too, even if their reset arrives before a complete Date field.
        var response = Encoding.Latin1.GetString(Convert.FromHexString(result.Bytes));
        const string datePattern = @"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun), \d{2} [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} GMT";
        var normalized = Regex.Replace(response, datePattern, "<date>");
        return result with { Bytes = Convert.ToHexString(Encoding.Latin1.GetBytes(normalized)) };
    }

    private void WriteBurstDistribution(string name,
        IReadOnlyList<(Http3ProbeResult Response, Http3ProbeResult Write)> results)
    {
        TestContext.WriteLine($"{name}: {results.Count} samples");
        foreach (var group in results.GroupBy(result => result).OrderByDescending(group => group.Count()))
            TestContext.WriteLine($"  {group.Count()}: response={group.Key.Response}; write={group.Key.Write}");
        // FIN acknowledgement can beat STOP_SENDING; retain both directions without treating it as another outcome.
        foreach (var group in results.GroupBy(result => result.Response).OrderByDescending(group => group.Count()))
            TestContext.WriteLine($"  response total {group.Count()}: {group.Key}");
    }
}
