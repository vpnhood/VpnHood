using System.Net.Quic;
using System.Text;
using System.Text.RegularExpressions;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(true, 32769)]
    [DataRow(true, 65536)]
    [DataRow(true, 65537)]
    [DataRow(false, 101)]
    [DataRow(false, 200)]
    [DataRow(false, 201)]
    public async Task Trailer_burst_distributions_match_clear_reference_401_majorities(bool octets, int total)
    {
        const int sampleCount = 30;
        var name = $"trailer-burst-{(octets ? "octets" : "count")}-{total}";
        var bytes = BuildTrailerBurst(octets, total);
        var cancellationToken = TestContext.CancellationToken;
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(Http3Probe.Timeout);
        // Reuse each connection so 360 samples need only twelve handshakes.
        await using var reference = await CreateProbe().Connect(Servers.ReferenceEndPoint, 3, startup.Token);
        await using var ours = await CreateProbe().Connect(Servers.OurEndPoint, 3, startup.Token);
        await using var referenceControl = await reference.OpenOutboundStreamAsync(QuicStreamType.Unidirectional,
            startup.Token);
        await using var ourControl = await ours.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, startup.Token);
        await referenceControl.WriteAsync(new byte[] { 0, 4, 0 }, startup.Token);
        await ourControl.WriteAsync(new byte[] { 0, 4, 0 }, startup.Token);
        var expected = new List<(Http3ProbeResult Response, Http3ProbeResult Write)>(sampleCount);
        var actual = new List<(Http3ProbeResult Response, Http3ProbeResult Write)>(sampleCount);
        try {
            for (var sample = 0; sample < sampleCount; sample++) {
                // Alternate the first server without adding concurrent load or a parser barrier.
                if (sample % 2 == 0) {
                    expected.Add(await ObserveTrailerBurst(reference, bytes, cancellationToken));
                    actual.Add(await ObserveTrailerBurst(ours, bytes, cancellationToken));
                }
                else {
                    actual.Add(await ObserveTrailerBurst(ours, bytes, cancellationToken));
                    expected.Add(await ObserveTrailerBurst(reference, bytes, cancellationToken));
                }
            }
        }
        finally {
            WriteBurstDistribution(name + " reference", expected);
            WriteBurstDistribution(name + " ours", actual);
        }

        // Burst odds depend on the host; the accepted difference stays diagnostic outside clear 401 majorities.
        var referenceMajority = expected.Where(result => IsCompleteBurstChallenge(result.Response))
            .GroupBy(result => result.Response).OrderByDescending(group => group.Count()).FirstOrDefault();
        const int clearMajorityCount = (sampleCount * 2 + 2) / 3;
        if (referenceMajority == null || referenceMajority.Count() < clearMajorityCount) {
            TestContext.WriteLine($"{name}: the reference's complete 401 outcome has " +
                $"{referenceMajority?.Count() ?? 0}/{sampleCount} samples, below the two-thirds threshold; " +
                "distributions are diagnostic only.");
            return;
        }

        var matching = actual.Count(result => result.Response == referenceMajority.Key);
        Assert.IsTrue(matching >= clearMajorityCount,
            $"{name}: ours matched the reference's {referenceMajority.Count()}/{sampleCount} 401 majority " +
            $"only {matching}/{sampleCount} times; at least {clearMajorityCount} required: {referenceMajority.Key}");
    }

    private static bool IsCompleteBurstChallenge(Http3ProbeResult result)
    {
        if (!result.Fin || result.Error != null || result.ApplicationErrorCode != null)
            return false;
        var bytes = Convert.FromHexString(result.Bytes);
        if (bytes.Length < 2 || bytes[0] != 1)
            return false;
        var payloadOffset = 1 + (1 << (bytes[1] >> 6));
        // RC3 starts HEADERS with a zero QPACK prefix and a literal :status at static name index 63.
        ReadOnlySpan<byte> challengePrefix = [0, 0, 0x5f, 0x30, 3, (byte)'4', (byte)'0', (byte)'1'];
        return bytes.Length >= payloadOffset && bytes.AsSpan(payloadOffset).StartsWith(challengePrefix);
    }

    private static byte[] BuildTrailerBurst(bool octets, int total)
    {
        var headers = octets ? CreateSizedFields(ValidationHeaders(), 32000, 8) : CreateCountedHeaders();
        IReadOnlyList<(string Name, string Value)> trailers = octets
            ? CreateSizedFields([], total - 32000, 8)
            : [.. Enumerable.Repeat(("x", ""), total - headers.Count)];
        return [.. BuildLiteralRequest(headers), .. BuildLiteralRequest(trailers)];
    }

    private static async Task<(Http3ProbeResult Response, Http3ProbeResult Write)> ObserveTrailerBurst(
        QuicConnection connection, byte[] bytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var response = ReadCompleteResponse(request, deadline.Token);
        var write = ObserveWriteEnd(request, deadline.Token);
        try {
            // A single native send leaves initial headers, trailers and FIN in the same uncontrolled burst.
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
        // FIN acknowledgement can beat STOP_SENDING, so only the complete response chooses the majority.
        foreach (var group in results.GroupBy(result => result.Response).OrderByDescending(group => group.Count()))
            TestContext.WriteLine($"  response total {group.Count()}: {group.Key}");
    }
}
