using System.Diagnostics;
using System.Net.Quic;
using System.Text;
using VpnHood.Test.KestrelParity.Probing;
using VpnHood.Test.KestrelParity.Servers;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(32768)]
    [DataRow(32769)]
    [DataRow(65536)]
    [DataRow(65537)]
    public async Task Aggregate_header_octet_boundaries_match_the_reference(int decodedBytes)
    {
        var fields = CreateSizedFields(ValidationHeaders(), decodedBytes, 8);
        await AssertRequestMatches($"aggregate-header-octets-{decodedBytes}", BuildLiteralRequest(fields),
            fragmented: false, withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    public async Task The_32_octets_per_field_band_matches_the_reference(int boundaryOffset)
    {
        const int totalFields = 12;
        var decodedBytes = 32768 - 32 * totalFields + boundaryOffset;
        var fields = CreateSizedFields(ValidationHeaders(), decodedBytes, totalFields - 4);
        Assert.AreEqual(totalFields, fields.Count);
        await AssertRequestMatches($"field-overhead-band-{boundaryOffset}", BuildLiteralRequest(fields),
            fragmented: false, withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow("trailers")]
    [DataRow("Trailers")]
    [DataRow("TRAILERS")]
    [DataRow("tRaIlErS")]
    public async Task Te_value_casing_matches_the_reference(string value)
    {
        var fields = ValidationHeaders().Append(("te", value)).ToArray();
        await AssertRequestMatches($"te-{value}", BuildLiteralRequest(fields), fragmented: false,
            withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(8191, false)]
    [DataRow(8192, false)]
    [DataRow(8193, false)]
    [DataRow(8191, true)]
    [DataRow(8192, true)]
    [DataRow(8193, true)]
    public async Task Combined_request_line_boundaries_match_the_reference(int length, bool spreadAcrossFields)
    {
        var method = spreadAcrossFields ? new string('M', 1024) : "GET";
        var authority = spreadAcrossFields ? new string('a', 2048) : Servers.HostName;
        const string scheme = "https";
        var path = "/" + new string('p', length - method.Length - scheme.Length - authority.Length - 1);
        IReadOnlyList<(string Name, string Value)> fields = [
            (":method", method), (":scheme", scheme), (":authority", authority), (":path", path)
        ];
        Assert.AreEqual(length, fields.Sum(field => field.Value.Length));
        await AssertRequestMatches($"request-line-{length}-spread-{spreadAcrossFields}", BuildLiteralRequest(fields),
            fragmented: false, withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow("empty-literal-name", new byte[] { 0, 0, 0x20, 0 })]
    [DataRow("nonzero-delta-base", new byte[] { 0, 1 })]
    [DataRow("signed-zero-delta-base", new byte[] { 0, 0x80 })]
    public async Task Qpack_empty_names_and_base_prefixes_match_the_reference(string name, byte[] block)
    {
        await AssertRequestMatches(name, Frame(1, block), fragmented: false, withholdFin: false,
            sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(128)]
    public async Task Qpack_base_prefixes_on_complete_requests_match_the_reference(int deltaBase)
    {
        // An otherwise valid request separates the Base decision from missing pseudo-headers.
        byte[] block = [0, (byte)deltaBase, 0xd1, 0xd7, 0xc1];
        await AssertRequestMatches($"complete-request-base-{deltaBase}", Frame(1, block), fragmented: false,
            withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(32768)]
    [DataRow(32769)]
    [DataRow(65536)]
    [DataRow(65537)]
    public async Task Header_and_trailer_octets_during_a_held_application_preserve_the_selected_response(int decodedBytes)
    {
        const int headerBytes = 32000;
        var headers = CreateSizedFields(ValidationHeaders(), headerBytes, 8);
        var trailers = CreateMarkedTrailerFields(decodedBytes - headerBytes);
        await AssertTrailersDuringHeldApplication($"headers-and-trailers-octets-{decodedBytes}",
            BuildLiteralRequest(headers), BuildLiteralRequest(trailers), [],
            decodedBytes > 65536 ? 0x10b : null, decodedBytes > 32768 ? "431" : "401",
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(100)]
    [DataRow(101)]
    [DataRow(200)]
    [DataRow(201)]
    public async Task Header_and_trailer_counts_during_a_held_application_preserve_the_selected_response(int totalCount)
    {
        var headers = CreateCountedHeaders();
        IReadOnlyList<(string Name, string Value)> trailers = Enumerable
            .Repeat(("x", ""), totalCount - headers.Count - 1)
            .Append((ReferenceRequestGate.MarkerName, "1"))
            .ToArray();
        Assert.AreEqual(totalCount, headers.Count + trailers.Count);
        await AssertTrailersDuringHeldApplication($"headers-and-trailers-count-{totalCount}",
            BuildLiteralRequest(headers), BuildLiteralRequest(trailers), [],
            totalCount > 200 ? 0x10e : null, totalCount > 100 ? "431" : "401",
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task Data_after_trailers_during_a_held_application_closes_the_connection()
    {
        var headers = BuildLiteralRequest(ValidationHeaders());
        byte[] tail = [.. BuildLiteralRequest(Enumerable.Repeat(("x", ""), 8).ToArray()), .. Frame(0, [1])];
        // No marker releases the reference application before its parser reaches the invalid DATA frame.
        var expected = await ObserveTrailersBeforeDispatch(headers, tail, [], TestContext.CancellationToken);
        var actual = await ObserveTrailersDuringHeldApplication(headers, tail, [], TestContext.CancellationToken);
        var error = new Http3ProbeResult("", false, QuicError.ConnectionAborted, 0x105);
        Assert.AreEqual(error, expected.Response);
        Assert.AreEqual(error, expected.Write);
        AssertRequestEnding("data-after-trailers-parser-first", expected, actual);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(32768)]
    [DataRow(32769)]
    [DataRow(65536)]
    [DataRow(65537)]
    public async Task Trailer_octet_limits_after_the_final_response_preserve_the_challenge(int decodedBytes)
    {
        var headers = BuildLiteralRequest(CreateSizedFields(ValidationHeaders(), 32000, 8));
        var trailers = BuildLiteralRequest(CreateMarkedTrailerFields(decodedBytes - 32000));
        await AssertTrailersAfterResponse($"trailer-octets-{decodedBytes}-response-first", headers, trailers,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(100)]
    [DataRow(101)]
    [DataRow(200)]
    [DataRow(201)]
    public async Task Trailer_count_limits_after_the_final_response_preserve_the_challenge(int totalCount)
    {
        var headers = CreateCountedHeaders();
        var trailers = Enumerable.Repeat(("x", ""), totalCount - headers.Count).ToArray();
        await AssertTrailersAfterResponse($"trailer-count-{totalCount}-response-first",
            BuildLiteralRequest(headers), BuildLiteralRequest(trailers), TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task A_trailer_hard_limit_after_a_selected_response_preserves_its_status(bool octets)
    {
        var headers = octets ? CreateSizedFields(ValidationHeaders(), 32000, 8) : CreateCountedHeaders();
        IReadOnlyList<(string Name, string Value)> prefixFields = octets
            ? CreateMarkedTrailerFields(65536 - 32000)
            : Enumerable.Repeat(("x", ""), 200 - headers.Count - 1)
                .Append((ReferenceRequestGate.MarkerName, "1")).ToArray();
        // The reference can select 431 before dispatch; a held application has already selected 401.
        var trailers = BuildLiteralRequest([.. prefixFields, ("x", "")]);
        byte[] tail = [0x21, (byte)'x', 0];
        CollectionAssert.AreEqual(tail, trailers[^tail.Length..]);
        await AssertTrailersDuringHeldApplication($"trailer-hard-limit-after-selected-response-octets-{octets}",
            BuildLiteralRequest(headers), trailers[..^tail.Length], tail, null, "431",
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Validation")]
    [DataRow("")]
    [DataRow("websocket")]
    [DataRow("webtransport")]
    public async Task Protocol_on_an_ordinary_get_matches_the_reference(string protocol)
    {
        var fields = ValidationHeaders().Append((":protocol", protocol)).ToArray();
        await AssertRequestMatches($"ordinary-get-protocol-{protocol}", BuildLiteralRequest(fields),
            fragmented: false, withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    private static IReadOnlyList<(string Name, string Value)> ValidationHeaders() => [
        (":method", "GET"), (":scheme", "https"), (":path", "/"), (":authority", Servers.HostName)
    ];

    private static IReadOnlyList<(string Name, string Value)> CreateCountedHeaders() =>
        ValidationHeaders().Concat(Enumerable.Repeat(("x", ""), 50 - ValidationHeaders().Count)).ToArray();

    private static IReadOnlyList<(string Name, string Value)> CreateMarkedTrailerFields(int decodedBytes)
    {
        var marker = (ReferenceRequestGate.MarkerName, "1");
        var fields = CreateSizedFields([marker], decodedBytes, 8);
        // The selector releases dispatch only after the final field has updated both shared totals.
        return fields.Skip(1).Append(marker).ToArray();
    }

    private async Task AssertTrailersDuringHeldApplication(string name, byte[] headers, byte[] trailers, byte[] lateTail,
        long? resetCode, string status, CancellationToken cancellationToken)
    {
        var expected = await ObserveTrailersBeforeDispatch(headers, trailers, lateTail, cancellationToken);
        var actual = await ObserveTrailersDuringHeldApplication(headers, trailers, lateTail, cancellationToken);
        if (resetCode != null) {
            Assert.AreEqual(new Http3ProbeResult("", false, QuicError.StreamAborted, resetCode), expected.Response);
            Assert.AreEqual(new Http3ProbeResult("", false, QuicError.StreamAborted, resetCode), expected.Write);
        }
        else {
            AssertTrailerResponse(expected, status);
        }
        if (resetCode != null) {
            AssertRequestEnding(name, expected, actual);
            return;
        }
        // Production selects before entering the responder, so holding emission freezes its ordinary 401.
        // Keep the reference's pre-dispatch 431 above, and compare this response with its selected-401 case.
        var responseFirst = await ObserveAfterResponse(Servers.ReferenceEndPoint, headers,
            [.. trailers, .. lateTail], cancellationToken);
        AssertTrailerResponse(responseFirst, "401");
        AssertRequestEnding(name + "-selected-401", responseFirst, actual);
    }

    private static async Task<RequestEnding> ObserveTrailersDuringHeldApplication(byte[] headers, byte[] trailers,
        byte[] lateTail, CancellationToken cancellationToken)
    {
        byte[] prefix = [.. headers, .. trailers];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var server = await GatedHttp3Server.Start(Servers.Certificate,
            prefix.Length, headers.Length, finish: false, deadline.Token);
        return lateTail.Length == 0
            ? await ObserveRequestEnding(server.EndPoint, prefix, finish: false, atomicFin: false, deadline.Token)
            : await ObserveAfterResponse(server.EndPoint, prefix, lateTail, deadline.Token);
    }

    private async Task AssertTrailersAfterResponse(string name, byte[] headers, byte[] trailers,
        CancellationToken cancellationToken)
    {
        var expected = await ObserveAfterResponse(Servers.ReferenceEndPoint, headers, trailers, cancellationToken);
        var actual = await ObserveAfterResponse(Servers.OurEndPoint, headers, trailers, cancellationToken);
        AssertTrailerResponse(expected, "401");
        AssertRequestEnding(name, expected, actual);
    }

    private static void AssertTrailerResponse(RequestEnding result, string status)
    {
        Assert.IsTrue(result.Response.Fin);
        Assert.IsNull(result.Response.Error);
        StringAssert.Contains(Encoding.ASCII.GetString(Convert.FromHexString(result.Response.Bytes)), status);
        Assert.AreEqual(new Http3ProbeResult("", false, QuicError.StreamAborted, 0x100), result.Write);
    }

    private static async Task<RequestEnding> ObserveTrailersBeforeDispatch(byte[] headers, byte[] trailers,
        byte[] lateTail, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        // A fresh endpoint keeps unrelated request dispatches out of the metric barrier.
        await using var reference = await GatedHttp3Reference.Start(Servers.Certificate, deadline.Token);
        using var gate = reference.Gate.Arm(deadline.Token);
        await using var connection = await CreateProbe().Connect(reference.EndPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var started = Stopwatch.GetTimestamp();
        var response = ObserveTimedResponseEnd(request, started, deadline.Token);
        var write = ObserveTimedWriteEnd(request, started, deadline.Token);
        try {
            await request.WriteAsync(headers, deadline.Token);
            await gate.WaitForDispatch(deadline.Token);
            await SendRequestTail(request, trailers, finish: false, deadline.Token);
            var completedResponse = await response;
            if (lateTail.Length > 0) {
                Assert.IsTrue(completedResponse.Result.Fin);
                await SendRequestTail(request, lateTail, finish: false, deadline.Token);
            }
            var sentAt = Stopwatch.GetElapsedTime(started);
            var completedWrite = await write;
            gate.Release();
            await gate.WaitForCompletion(deadline.Token);
            return new RequestEnding(completedResponse.Result, completedWrite.Result, sentAt,
                completedResponse.Elapsed, completedWrite.Elapsed);
        }
        finally {
            gate.Release();
            if (gate.WasEntered) {
                // Cleanup must still join the synchronous callback after the probe's token has expired.
                using var cleanup = new CancellationTokenSource(Http3Probe.Timeout);
                try { await gate.WaitForCompletion(cleanup.Token); }
                catch (Exception) {
                    // The successful path already checked completion; retain the original failure here.
                }
            }
            await deadline.CancelAsync();
            try { await Task.WhenAll(response, write); }
            catch (Exception) {
                // Join both observers before their native stream is disposed, preserving the first failure.
            }
        }
    }

    private static IReadOnlyList<(string Name, string Value)> CreateSizedFields(
        IReadOnlyList<(string Name, string Value)> prefix, int decodedBytes, int extraFields)
    {
        const string name = "x-size";
        var fields = new List<(string Name, string Value)>(prefix);
        var remaining = decodedBytes - prefix.Sum(field => field.Name.Length + field.Value.Length) -
                        name.Length * extraFields;
        Assert.IsTrue(remaining >= 0);
        for (var i = 0; i < extraFields; i++) {
            var valueLength = remaining / (extraFields - i);
            Assert.IsTrue(valueLength <= 32768, "Aggregate limits must not trip the individual string limit.");
            fields.Add((name, new string('a', valueLength)));
            remaining -= valueLength;
        }
        Assert.AreEqual(decodedBytes, fields.Sum(field => field.Name.Length + field.Value.Length));
        return fields;
    }
}
