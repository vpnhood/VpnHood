using System.Net;
using System.Net.Quic;
using System.Diagnostics;
using VpnHood.Net.Http3;
using VpnHood.Test.KestrelParity.Probing;
using VpnHood.Test.KestrelParity.Servers;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("short-body", "4", true)]
    [DataRow("long-body", "2", false)]
    public async Task Data_length_mismatches_match_both_terminal_stream_directions(string name, string contentLength,
        bool requiresFin)
    {
        byte[] request = [.. BuildBodyHeaders(contentLength), .. Frame(0, [1, 2, 3])];
        await ObserveDataLengthTiming(name, request, TestContext.CancellationToken);
        // Hold the reference application; the pinned fixture sees the complete buffered request.
        byte[] heldRequest = [.. BuildBodyHeaders(contentLength, waitForAbort: true), .. Frame(0, [1, 2, 3])];
        var expected = await ObserveRequestEnding(Servers.ReferenceEndPoint, heldRequest, requiresFin,
            atomicFin: true, TestContext.CancellationToken);
        var actual = await ObserveBufferedDataLength(heldRequest, requiresFin,
            TestContext.CancellationToken);
        Assert.AreEqual(QuicError.StreamAborted, expected.Response.Error);
        Assert.AreEqual(0x101L, expected.Response.ApplicationErrorCode);
        Assert.IsEmpty(expected.Response.Bytes, "The held reference application cannot send a response first.");
        if (!requiresFin) {
            // Excess DATA is detectable while the client write side remains open: both resets are observable.
            Assert.AreEqual(QuicError.StreamAborted, expected.Write.Error);
            Assert.AreEqual(0x101L, expected.Write.ApplicationErrorCode);
            AssertRequestEnding(name + "-parser-first", expected, actual);
            return;
        }
        TestContext.WriteLine($"{name}-parser-first: reference={expected}; ours={actual}");
        AssertSame(name + " response", expected.Response, actual.Response);
        // Shortage requires FIN. Its transport acknowledgement can beat the HTTP parser's STOP_SENDING,
        // so an already completed client write cannot establish which stop code the server chose.
        AssertFinishedShortBodyWrite(expected.Write);
        AssertFinishedShortBodyWrite(actual.Write);
    }

    private static async Task<RequestEnding> ObserveBufferedDataLength(byte[] request, bool finish,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var server = await PinnedHttp3Server.Start(Servers.Certificate,
            new Http3TrailerSchedule(long.MaxValue, double.PositiveInfinity, double.PositiveInfinity), deadline.Token);
        // Prefetch FIN as well when shortage needs it, so available-only inspection reaches the mismatch.
        using var buffered = server.BufferedQuic.BufferNextRequest(request.Length, static () => { }, finish,
            deadline.Token);
        return await ObserveRequestEnding(server.EndPoint, request, finish, atomicFin: true, deadline.Token);
    }

    private async Task ObserveDataLengthTiming(string name, byte[] request, CancellationToken cancellationToken)
    {
        // These are diagnostics, not parity evidence: RC3 runs its application beside DATA/FIN parsing.
        // Compare the controlled parser-first and response-first cases instead of two independent races.
        foreach (var atomicFin in new[] { false, true }) {
            var results = await Task.WhenAll(
                ObserveWholeRequest(Servers.ReferenceEndPoint, request, atomicFin, cancellationToken),
                ObserveWholeRequest(Servers.OurEndPoint, request, atomicFin, cancellationToken));
            TestContext.WriteLine($"{name}-natural-schedule-atomic-fin-{atomicFin} (diagnostic only): " +
                                  $"reference={results[0]}; ours={results[1]}");
        }
    }

    private static void AssertFinishedShortBodyWrite(Http3ProbeResult write)
    {
        var expected = write.Fin
            ? new Http3ProbeResult("", true, null, null)
            : new Http3ProbeResult("", false, QuicError.StreamAborted, 0x101);
        Assert.AreEqual(expected, write, "A short body must finish sending or observe H3_GENERAL_PROTOCOL_ERROR.");
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("short-body", "4")]
    [DataRow("long-body", "2")]
    public async Task Data_length_mismatches_after_the_final_response_preserve_the_response(string name,
        string contentLength)
    {
        var headers = BuildBodyHeaders(contentLength);
        var tail = Frame(0, [1, 2, 3]);
        var expected = await ObserveAfterResponse(Servers.ReferenceEndPoint, headers, tail,
            TestContext.CancellationToken);
        var actual = await ObserveAfterPinnedResponse(headers, tail,
            TestContext.CancellationToken);
        Assert.AreEqual(QuicError.StreamAborted, expected.Write.Error);
        Assert.AreEqual(0x100L, expected.Write.ApplicationErrorCode);
        AssertRequestEnding(name + "-response-first", expected, actual);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_truncated_data_frame_counts_only_the_body_bytes_that_arrive(bool finish)
    {
        // The DATA envelope promises ten bytes, but only three arrive and Content-Length is three.
        // Counting the promise as received data would select the wrong error before the peer finishes.
        byte[] request = [.. BuildBodyHeaders("3"), 0, 10, 1, 2, 3];
        var expected = await ObserveRequestEnding(Servers.ReferenceEndPoint, request, finish,
            atomicFin: false, TestContext.CancellationToken);
        var actual = await ObserveRequestEnding(Servers.OurEndPoint, request, finish,
            atomicFin: false, TestContext.CancellationToken);
        TestContext.WriteLine($"truncated-data-frame-finish-{finish}: reference={expected}; ours={actual}");
        AssertSame("truncated-data-frame response", expected.Response, actual.Response);

        // An acknowledged client FIN can complete WritesClosed before STOP_SENDING arrives. Only that
        // row permits normal completion; leaving writes open must expose H3_NO_ERROR on both servers.
        var stoppedWrite = new Http3ProbeResult("", false, QuicError.StreamAborted, 0x100);
        foreach (var write in new[] { expected.Write, actual.Write }) {
            var expectedWrite = finish && write.Fin
                ? new Http3ProbeResult("", true, null, null)
                : stoppedWrite;
            Assert.AreEqual(expectedWrite, write, "A truncated DATA frame must finish sending or observe H3_NO_ERROR.");
        }
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("explicit-length", "3")]
    [DataRow("no-length", null)]
    public async Task A_body_with_an_exact_or_omitted_length_receives_the_reference_challenge(string name,
        string? contentLength)
    {
        byte[] request = [.. BuildBodyHeaders(contentLength), .. Frame(0, [1, 2, 3])];
        await AssertResponseMatches(name, request, [], null, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task Ordinary_trailers_after_data_leave_the_request_valid()
    {
        byte[] request = [.. BuildBodyHeaders("3"), .. Frame(0, [1, 2, 3]),
            .. BuildLiteralRequest([("x-trailer", "complete")])];
        await AssertResponseMatches("ordinary-trailers", request, [], null, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("pseudo-header-in-trailers")]
    [DataRow("data-after-trailers")]
    public async Task Invalid_trailers_are_observed_after_the_final_response(string name)
    {
        byte[] tail = name == "pseudo-header-in-trailers"
            ? BuildLiteralRequest([(":path", "/")])
            : [.. BuildLiteralRequest([("x-trailer", "complete")]), .. Frame(0, [1])];
        var expected = await ObserveAfterResponse(Servers.ReferenceEndPoint, tail, TestContext.CancellationToken);
        var actual = await ObserveAfterPinnedResponse(BuildBodyHeaders(null), tail, TestContext.CancellationToken);
        AssertRequestEnding(name, expected, actual);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow(2)]
    [DataRow(6)]
    [DataRow(8)]
    [DataRow(9)]
    public async Task Http2_only_frames_after_headers_match_the_reference(int frameType)
    {
        var tail = Frame((ulong)frameType, []);
        var expected = await ObserveAfterResponse(Servers.ReferenceEndPoint, tail, TestContext.CancellationToken);
        var actual = await ObserveAfterPinnedResponse(BuildBodyHeaders(null), tail, TestContext.CancellationToken);
        AssertRequestEnding($"reserved-request-frame-{frameType}", expected, actual);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow(2)]
    [DataRow(6)]
    [DataRow(8)]
    [DataRow(9)]
    public async Task Coalesced_http2_only_request_frames_match_before_and_after_headers(int frameType)
    {
        var reserved = Frame((ulong)frameType, []);
        var headers = BuildBodyHeaders(null);
        // These writes arrive together, before the application can stop reading after its 401.
        await AssertRequestMatches($"reserved-{frameType}-before", [.. reserved, .. headers], fragmented: false,
            withholdFin: false, sendFin: true, TestContext.CancellationToken);
        await AssertRequestMatches($"reserved-{frameType}-after", [.. headers, .. reserved], fragmented: false,
            withholdFin: false, sendFin: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task A_control_stream_ending_inside_settings_matches_the_reference()
    {
        // SETTINGS declares four payload bytes, but FIN arrives during the value's four-byte integer.
        await AssertControlEnding("truncated-settings", [0, 4, 4, 6, 0x80], finish: true,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow(9_999)]
    [DataRow(10_000)]
    [DataRow(70_000)]
    public async Task Unknown_control_frame_size_boundaries_match_the_reference(int payloadLength)
    {
        // DATA after the extension gives a terminal result. A cap that rejects the unknown payload
        // itself would report a different error, even if a concurrent request had already received 401.
        byte[] bytes = [0, 4, 0, .. Frame(0x21, new byte[payloadLength]), 0, 0];
        await AssertControlEnding($"unknown-control-{payloadLength}", bytes, finish: false,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("increasing-goaway", new byte[] { 7, 1, 1, 7, 1, 2 })]
    [DataRow("decreasing-max-push-id", new byte[] { 0x0d, 1, 2, 0x0d, 1, 1 })]
    [DataRow("cancel-unpromised-push", new byte[] { 3, 1, 0 })]
    public async Task Control_identifier_order_matches_the_reference(string name, byte[] sequence)
    {
        // The reference may reject the identifier order or ignore an unsupported push operation.
        // A following forbidden DATA frame guarantees an actual result in either case, never silence.
        await AssertControlEnding(name, [0, 4, 0, .. sequence, 0, 0], finish: false,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task Reserved_http2_control_types_reach_the_reference_size_error()
    {
        // Kestrel ignores these types on a control stream. A later oversized unknown frame gives
        // H3_FRAME_ERROR, distinct from rejecting any reserved type with H3_FRAME_UNEXPECTED.
        byte[] bytes = [0, 4, 0, 2, 0, 6, 0, 8, 0, 9, 0, .. Frame(0x21, new byte[10_000])];
        await AssertControlEnding("reserved-control-types", bytes, finish: false, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task Setting_zero_is_rejected_as_a_reserved_identifier()
    {
        await AssertControlEnding("reserved-setting-zero", [0, 4, 2, 0, 1, 0, 0], finish: false,
            TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task Unimplemented_extension_values_match_the_reference_acceptance()
    {
        // Neither CONNECT nor datagrams are enabled by this front. The reference nevertheless accepts
        // their non-boolean values; the later size error proves both setting pairs were processed.
        byte[] bytes = [0, 4, 4, 8, 2, 0x33, 2, .. Frame(0x21, new byte[10_000])];
        await AssertControlEnding("extension-setting-values", bytes, finish: false, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("nonzero-encoder-capacity", new byte[] { 2, 0x21 })]
    [DataRow("encoder-insert", new byte[] { 2, 0x41, 0x61, 1, 0x62 })]
    [DataRow("decoder-acknowledgment", new byte[] { 3, 0x80 })]
    public async Task Qpack_instructions_are_drained_until_the_critical_stream_closes(string name, byte[] bytes)
    {
        // FIN makes the drain observable as H3_CLOSED_CRITICAL_STREAM. A parser rejecting these
        // instructions early would return a QPACK error instead; silence never establishes parity.
        await AssertControlEnding(name, bytes, finish: true, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Http3Framing")]
    public async Task An_unsupported_peer_push_stream_does_not_close_a_normal_request()
    {
        await AssertResponseMatches("unsupported-peer-push", BuildQpackRequest([]), [], [1, 0],
            TestContext.CancellationToken);
    }

    private static byte[] BuildBodyHeaders(string? contentLength, bool waitForAbort = false)
    {
        List<(string Name, string Value)> fields = [
            (":method", "POST"), (":scheme", "https"), (":path", "/probe"), (":authority", Servers.HostName)
        ];
        if (contentLength != null)
            fields.Add(("content-length", contentLength));
        if (waitForAbort)
            fields.Add(("x-parity-wait-for-abort", "1"));
        return BuildLiteralRequest(fields);
    }

    private async Task AssertControlEnding(string name, byte[] bytes, bool finish, CancellationToken cancellationToken)
    {
        var expected = await ObserveControlEnding(Servers.ReferenceEndPoint, bytes, finish, cancellationToken);
        var actual = await ObserveControlEnding(Servers.OurEndPoint, bytes, finish, cancellationToken);
        Assert.AreEqual(QuicError.ConnectionAborted, expected.Error, "The reference must report a connection result.");
        AssertSame(name, expected, actual);
    }

    private static async Task<Http3ProbeResult> ObserveControlEnding(IPEndPoint endPoint, byte[] bytes, bool finish,
        CancellationToken cancellationToken)
    {
        try {
            return await CreateProbe().Control(endPoint, bytes, duplicate: false, finish, cancellationToken);
        }
        catch (QuicException ex) {
            // A large write can observe the peer's close before the helper reaches its accept loop.
            return new Http3ProbeResult("", false, ex.QuicError, ex.ApplicationErrorCode);
        }
    }

    private static Task<RequestEnding> ObserveWholeRequest(IPEndPoint endPoint, byte[] bytes,
        CancellationToken cancellationToken) => ObserveWholeRequest(endPoint, bytes, atomicFin: false, cancellationToken);

    private static Task<RequestEnding> ObserveWholeRequest(IPEndPoint endPoint, byte[] bytes, bool atomicFin,
        CancellationToken cancellationToken) => ObserveRequestEnding(endPoint, bytes, finish: true, atomicFin,
        cancellationToken);

    private static async Task<RequestEnding> ObserveRequestEnding(IPEndPoint endPoint, byte[] bytes, bool finish,
        bool atomicFin,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var started = Stopwatch.GetTimestamp();
        var response = ObserveTimedResponseEnd(request, started, deadline.Token);
        var write = ObserveTimedWriteEnd(request, started, deadline.Token);
        try {
            if (finish && atomicFin)
                await SendAtomicRequest(request, bytes, deadline.Token);
            else
                await SendRequestTail(request, bytes, finish, deadline.Token);
            var sentAt = Stopwatch.GetElapsedTime(started);
            await Task.WhenAll(response, write);
            var completedResponse = await response;
            var completedWrite = await write;
            return new RequestEnding(completedResponse.Result, completedWrite.Result, sentAt,
                completedResponse.Elapsed, completedWrite.Elapsed);
        }
        finally {
            await deadline.CancelAsync();
            try { await Task.WhenAll(response, write); }
            catch (Exception) {
                // Keep the original failure while joining both observers before stream disposal.
            }
        }
    }

    private static Task<RequestEnding> ObserveAfterResponse(IPEndPoint endPoint, byte[] tail,
        CancellationToken cancellationToken) => ObserveAfterResponse(endPoint, BuildBodyHeaders(null), tail,
        cancellationToken);

    private static async Task<RequestEnding> ObserveAfterPinnedResponse(byte[] headers, byte[] tail,
        CancellationToken cancellationToken)
    {
        await using var server = await PinnedHttp3Server.Start(Servers.Certificate,
            new Http3TrailerSchedule(0, 0, 0), cancellationToken);
        return await ObserveAfterResponse(server.EndPoint, headers, tail, cancellationToken);
    }

    private static async Task<RequestEnding> ObserveAfterResponse(IPEndPoint endPoint, byte[] headers, byte[] tail,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var started = Stopwatch.GetTimestamp();
        var response = ObserveTimedResponseEnd(request, started, deadline.Token);
        var write = ObserveTimedWriteEnd(request, started, deadline.Token);
        try {
            await request.WriteAsync(headers, deadline.Token);
            var completedResponse = await response;
            Assert.IsTrue(completedResponse.Result.Fin,
                "The protected application must finish its response without waiting for the body.");
            Assert.IsNotEmpty(completedResponse.Result.Bytes);

            // Keeping the write side open separates STOP_SENDING from an acknowledged client FIN.
            await SendRequestTail(request, tail, finish: false, deadline.Token);
            var sentAt = Stopwatch.GetElapsedTime(started);
            var completedWrite = await write;
            return new RequestEnding(completedResponse.Result, completedWrite.Result, sentAt,
                completedResponse.Elapsed, completedWrite.Elapsed);
        }
        finally {
            await deadline.CancelAsync();
            try { await Task.WhenAll(response, write); }
            catch (Exception) {
                // Keep the original failure while joining both observers before stream disposal.
            }
        }
    }

    private static async Task SendAtomicRequest(QuicStream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        try {
            // One native send removes the gap between DATA being queued and FIN being queued.
            await stream.WriteAsync(bytes, completeWrites: true, cancellationToken);
        }
        catch (QuicException) {
            // The read and WritesClosed observers retain both directions when a peer rejects early.
        }
    }

    private static async Task<(Http3ProbeResult Result, TimeSpan Elapsed)> ObserveTimedWriteEnd(QuicStream stream,
        long started, CancellationToken cancellationToken)
    {
        var result = await ObserveWriteEnd(stream, cancellationToken);
        return (result, Stopwatch.GetElapsedTime(started));
    }

    private static async Task<(Http3ProbeResult Result, TimeSpan Elapsed)> ObserveTimedResponseEnd(QuicStream stream,
        long started, CancellationToken cancellationToken)
    {
        var result = await ReadCompleteResponse(stream, cancellationToken);
        return (result, Stopwatch.GetElapsedTime(started));
    }

    private static async Task SendRequestTail(QuicStream stream, byte[] bytes, bool finish,
        CancellationToken cancellationToken)
    {
        try {
            await stream.WriteAsync(bytes, cancellationToken);
            if (finish)
                stream.CompleteWrites();
        }
        catch (QuicException) {
            // Observe both terminal directions below, even if the reset overtakes the write itself.
        }
    }

    private static async Task<Http3ProbeResult> ReadCompleteResponse(QuicStream stream, CancellationToken cancellationToken)
    {
        using var response = new MemoryStream();
        var buffer = new byte[4096];
        try {
            while (true) {
                var count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                    return new Http3ProbeResult(Convert.ToHexString(response.ToArray()), true, null, null);
                Assert.IsTrue(response.Length + count <= 4096, "Unexpected response size for an empty challenge.");
                response.Write(buffer, 0, count);
            }
        }
        catch (QuicException ex) {
            return new Http3ProbeResult(Convert.ToHexString(response.ToArray()), false, ex.QuicError, ex.ApplicationErrorCode);
        }
    }

    private static async Task<Http3ProbeResult> ObserveWriteEnd(QuicStream stream, CancellationToken cancellationToken)
    {
        try {
            await stream.WritesClosed.WaitAsync(cancellationToken);
            return new Http3ProbeResult("", true, null, null);
        }
        catch (QuicException ex) {
            return new Http3ProbeResult("", false, ex.QuicError, ex.ApplicationErrorCode);
        }
    }

    private void AssertRequestEnding(string name, RequestEnding expected, RequestEnding actual)
    {
        TestContext.WriteLine($"{name}: reference={expected}; ours={actual}");
        AssertSame(name + " response", expected.Response, actual.Response);
        Assert.AreEqual(expected.Write, actual.Write, name + " write-side completion");
    }
}
