using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3ResponseDispatchTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Response_completion_stops_the_parser_before_a_native_abort_resumes_it_inline()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        await using var output = new Http3PendingWriteStream(lifetime.Token);
        await using var quic = new Http3InlineAbortQuicStream(output);
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(quic, quic: quic);
        input.BeginBodyInspection(options.MaxBodyInspectionBytes);
        var failures = new List<Exception>();
        await using var dispatch = new Http3ResponseDispatch(quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, failures.Add,
            input, CreateHeaders(options));
        var inspection = ObservePendingRead(input, dispatch, quic, lifetime.Token);
        Assert.IsTrue(quic.HasPendingRead);
        dispatch.Start(lifetime.Token);
        try {
            await output.WaitForWrite(lifetime.Token);
            Assert.IsFalse(inspection.IsCompleted,
                "An in-flight read remains owned while the native write is pending.");
            output.ReleaseWrite();
            Assert.IsTrue(await inspection.WaitAsync(lifetime.Token),
                "The pending parser must observe its stop inside the native AbortRead call.");
            Assert.IsEmpty(failures);
            await dispatch.Join(lifetime.Token);

            Assert.AreEqual("401", await ReadStatus(output.ToArray(), lifetime.Token));
            Assert.AreEqual(1, quic.CompletedWrites);
            Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
            Assert.IsNull(quic.WriteAbortCode);
            Assert.IsEmpty(failures);
        }
        finally {
            output.ReleaseWrite();
        }
    }

    [TestMethod]
    public async Task An_inflight_hard_trailer_overrun_remains_visible_while_the_native_write_is_pending()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        await using var output = new Http3PendingWriteStream(lifetime.Token);
        var quic = new Http3RecordingQuicStream(output);
        // Two trailer fields follow three initial fields. Only the final trailer exceeds the
        // hard count of four. Its native read starts before response submission and completes
        // while the final response write is still pending.
        byte[] trailers = [0, 0, 0x21, (byte)'x', 0, 0x21, (byte)'x', 0];
        await using var source = new Http3GatedReadStream();
        await using var reader = new Http3StreamReader(source, quic: quic);
        reader.BeginBodyInspection(64 * 1024);
        var options = new Http3ServerOptions { MaxHeaderCount = 2 };
        var budget = new Http3HeaderBudget(options);
        for (var i = 0; i < 3; i++)
            budget.Append(new KeyValuePair<string, string>("header", ""));

        var section = Http3Qpack.ReadSection(reader, trailers.Length, null, options, budget, lifetime.Token);
        await source.WaitForRead(lifetime.Token);
        var write = quic.WriteFinal(new byte[] { 1 }, lifetime.Token).AsTask();
        try {
            await output.WaitForWrite(lifetime.Token);
            reader.ResponseSubmitted();
            source.Release(trailers);
            var error = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() => section);

            Assert.AreEqual(Http3Protocol.MessageError, error.ErrorCode);
            Assert.IsFalse(write.IsCompleted, "Parsing must not wait for native response completion.");
            Assert.IsNull(quic.ReadAbortCode, "Already available bytes must not be discarded at submission.");
        }
        finally {
            source.Release(trailers);
            output.ReleaseWrite();
            await write.WaitAsync(lifetime.Token);
            reader.CompleteResponse();
        }
    }

    [TestMethod]
    public async Task Trailer_fields_seen_before_selection_choose_a_limit_response()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        var options = new Http3ServerOptions { MaxHeaderCount = 4 };
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        var headers = CreateHeaders(options);
        var budget = new Http3HeaderBudget(options);
        foreach (var field in headers.Fields)
            budget.Append(field);
        budget.Append(new KeyValuePair<string, string>("trailer", ""));
        var responder = new Http3RecordingResponder();
        await using var dispatch = new Http3ResponseDispatch(quic, responder,
            new Http3Request([], null), budget, options, error => Assert.Fail(error.ToString()),
            input, headers);

        dispatch.Start(lifetime.Token);
        await dispatch.Join(lifetime.Token);

        Assert.IsNull(responder.Request, "The application must not run after the soft header limit is exceeded.");
        Assert.AreEqual("431", await ReadStatus(output.ToArray(), lifetime.Token));
    }

    [TestMethod]
    public async Task Request_metadata_is_validated_before_header_limit_selection()
    {
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        var options = new Http3ServerOptions { MaxHeaderCount = 4 };
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        var headers = CreateHeaders(options, method: "");
        var budget = new Http3HeaderBudget(options);
        foreach (var field in headers.Fields)
            budget.Append(field);
        budget.Append(new KeyValuePair<string, string>("trailer", ""));
        var responder = new Http3RecordingResponder();
        var failures = new List<Exception>();
        await using var dispatch = new Http3ResponseDispatch(quic, responder,
            new Http3Request([], null), budget, options, failures.Add,
            input, headers);

        dispatch.Start(TestContext.CancellationToken);
        var actual = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(
            () => dispatch.Join(TestContext.CancellationToken));

        Assert.AreEqual(Http3Protocol.ProtocolError, actual.ErrorCode);
        Assert.AreSame(failures.Single(), actual);
        Assert.AreEqual(0L, output.Length);
        Assert.IsNull(responder.Request);
    }

    [TestMethod]
    public async Task Async_disposal_waits_for_a_native_write_and_an_inflight_abort_callback()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var releaseAbort = new ManualResetEventSlim();
        var abortStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var output = new Http3PendingWriteStream(lifetime.Token);
        var quic = new Http3RecordingQuicStream(output);
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        var error = new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);
        await using var dispatch = new Http3ResponseDispatch(quic,
            new Http3RecordingResponder(), new Http3Request([], null), new Http3HeaderBudget(options), options, _ => {
                abortStarted.TrySetResult();
                releaseAbort.Wait(lifetime.Token);
            }, input, CreateHeaders(options));
        dispatch.Start(lifetime.Token);
        await output.WaitForWrite(lifetime.Token);
        var abort = Task.Run(() => dispatch.Fail(error), lifetime.Token);
        try {
            await abortStarted.Task.WaitAsync(lifetime.Token);
            output.ReleaseWrite();
            await output.WaitForWriteEnd(lifetime.Token);
            var disposal = dispatch.DisposeAsync().AsTask();

            Assert.IsFalse(disposal.IsCompleted, "The stream must remain owned until its abort callback returns.");
            releaseAbort.Set();
            await abort.WaitAsync(lifetime.Token);
            await disposal.WaitAsync(lifetime.Token);
            Assert.AreSame(error, await Assert.ThrowsExactlyAsync<Http3ProtocolException>(
                () => dispatch.Join(lifetime.Token)));
        }
        finally {
            releaseAbort.Set();
            output.ReleaseWrite();
            await abort.WaitAsync(lifetime.Token);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_queued_response_aborts_before_canceling_and_joins_its_pending_write(bool headerDeadline)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        await using var output = new Http3PendingWriteStream(lifetime.Token);
        var quic = new Http3RecordingQuicStream(output);
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        var failures = new List<Exception>();
        await using var dispatch = new Http3ResponseDispatch(quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, error => {
                var code = error is Http3ProtocolException protocol ? protocol.ErrorCode : Http3Protocol.RequestRejected;
                quic.AbortRead(code);
                quic.AbortWrite(code);
                failures.Add(error);
            }, input, CreateHeaders(options));
        dispatch.Start(headers.Token);
        var response = dispatch.Join(lifetime.Token);
        try {
            await output.WaitForWrite(lifetime.Token);
            if (headerDeadline)
                await headers.CancelAsync();
            else
                dispatch.Fail(new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false));
            await output.WaitForCancellation(lifetime.Token);

            Assert.AreEqual(headerDeadline ? Http3Protocol.RequestRejected : Http3Protocol.MessageError,
                quic.WriteAbortCode, "The protocol error must precede native cancellation.");
            Assert.HasCount(1, failures);
            Assert.IsFalse(response.IsCompleted, "Cancellation is not completion of the native write.");
            output.ReleaseWrite();
            if (headerDeadline) {
                var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => response);
                Assert.AreSame(failures.Single(), actual);
                Assert.AreEqual(headers.Token, actual.CancellationToken);
            }
            else {
                var actual = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() => response);
                Assert.AreSame(failures.Single(), actual);
            }
        }
        finally {
            output.ReleaseWrite();
            try { await response.WaitAsync(lifetime.Token); }
            catch (Exception) {
                // The assertion owns the failure; cleanup still waits for native completion.
            }
        }
    }

    [TestMethod]
    public async Task Selection_keeps_its_result_when_later_fields_cross_the_soft_limit()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        var options = new Http3ServerOptions { MaxHeaderCount = 4 };
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        var headers = CreateHeaders(options);
        var budget = new Http3HeaderBudget(options);
        foreach (var field in headers.Fields)
            budget.Append(field);
        var gate = new Http3RequestTestGate();
        await using var dispatch = new Http3ResponseDispatch(quic,
            new Http3GatedResponder(gate), new Http3Request([], null), budget, options,
            error => Assert.Fail(error.ToString()), input, headers);

        dispatch.Start(lifetime.Token);
        await gate.WaitForApplication(lifetime.Token);
        budget.Append(new KeyValuePair<string, string>("trailer", ""));
        gate.InputConsumed();
        await dispatch.Join(lifetime.Token);

        Assert.IsTrue(budget.ExceedsRequestLimits);
        Assert.AreEqual("401", await ReadStatus(output.ToArray(), lifetime.Token));
    }

    [TestMethod]
    public async Task A_protocol_failure_prevents_the_response_and_precedes_caller_cancellation()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        var responder = new Http3RecordingResponder();
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        var failures = new List<Exception>();
        await using var dispatch = new Http3ResponseDispatch(quic, responder, new Http3Request([], null),
            new Http3HeaderBudget(options), options, failures.Add, input, CreateHeaders(options));
        var failure = new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);

        dispatch.Fail(failure);
        dispatch.Start(lifetime.Token);
        Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() => dispatch.Join(lifetime.Token)));
        await lifetime.CancelAsync();
        dispatch.Fail(new OperationCanceledException(lifetime.Token));
        Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() => dispatch.Join(lifetime.Token)));

        Assert.AreSame(failure, failures.Single());
        Assert.AreEqual(0L, output.Length);
        Assert.IsNull(responder.Request);
        Assert.AreEqual(0, quic.CompletedWrites);
    }

    [TestMethod]
    public async Task A_failing_error_callback_preserves_the_protocol_failure_and_can_be_disposed()
    {
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        await using var dispatch = new Http3ResponseDispatch(quic,
            new Http3RecordingResponder(), new Http3Request([], null), new Http3HeaderBudget(options), options,
            _ => throw new InvalidOperationException("The transport callback failed."), input, CreateHeaders(options));
        var failure = new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);

        dispatch.Fail(failure);
        dispatch.Start(TestContext.CancellationToken);
        Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<Http3ProtocolException>(
            () => dispatch.Join(TestContext.CancellationToken)));
        await dispatch.DisposeAsync();

        Assert.AreEqual(0L, output.Length);
    }

    [TestMethod]
    public async Task Async_disposal_keeps_waiting_for_a_canceled_native_write()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        await using var output = new Http3PendingWriteStream(lifetime.Token);
        var quic = new Http3RecordingQuicStream(output);
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        var failures = new List<Exception>();
        await using var dispatch = new Http3ResponseDispatch(quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, failures.Add, input, CreateHeaders(options));
        dispatch.Start(headers.Token);
        try {
            await output.WaitForWrite(lifetime.Token);
            await headers.CancelAsync();
            await output.WaitForCancellation(lifetime.Token);
            var disposal = dispatch.DisposeAsync().AsTask();

            Assert.IsFalse(disposal.IsCompleted, "Disposal must not abandon the native write.");
            output.ReleaseWrite();
            await disposal.WaitAsync(lifetime.Token);
            var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => dispatch.Join(lifetime.Token));

            Assert.AreSame(failures.Single(), actual);
            Assert.AreEqual(headers.Token, actual.CancellationToken);
            Assert.AreEqual(1, quic.CompletedWrites);
        }
        finally {
            output.ReleaseWrite();
        }
    }

    [TestMethod]
    public async Task A_buffered_connection_error_still_closes_the_connection_after_response_fin_submission()
    {
        var gate = new Http3RequestTestGate();
        await using var output = new Http3SignaledOutputStream(gate);
        var quic = new Http3RecordingQuicStream(output);
        var failures = new List<Exception>();
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        await using var dispatch = new Http3ResponseDispatch(quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, failures.Add, input, CreateHeaders(options));

        dispatch.Start(TestContext.CancellationToken);
        await gate.WaitForResponse(TestContext.CancellationToken);
        var error = new Http3ProtocolException(Http3Protocol.FrameUnexpected);
        dispatch.Fail(error);
        Assert.AreSame(error, await Assert.ThrowsExactlyAsync<Http3ProtocolException>(
            () => dispatch.Join(TestContext.CancellationToken)));

        Assert.AreEqual(1, quic.CompletedWrites);
        Assert.AreSame(error, failures.Single());
    }

    [TestMethod]
    public async Task A_hard_trailer_error_after_native_fin_still_aborts_before_the_parser_joins()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        var failures = new List<Exception>();
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        await using var dispatch = new Http3ResponseDispatch(quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, error => {
                quic.AbortWrite(((Http3ProtocolException)error).ErrorCode);
                failures.Add(error);
            }, input, CreateHeaders(options));

        dispatch.Start(lifetime.Token);
        await dispatch.ResponseCompleted.WaitAsync(lifetime.Token);
        var error = new Http3ProtocolException(Http3Protocol.RequestRejected, connectionError: false);
        dispatch.Fail(error);

        Assert.AreSame(error, await Assert.ThrowsExactlyAsync<Http3ProtocolException>(
            () => dispatch.Join(lifetime.Token)));
        Assert.AreSame(error, failures.Single());
        Assert.AreEqual(Http3Protocol.RequestRejected, quic.WriteAbortCode);
        Assert.AreEqual(1, quic.CompletedWrites);
    }

    [TestMethod]
    public async Task A_joined_dispatch_does_not_accept_a_late_connection_error()
    {
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        var failures = new List<Exception>();
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(Stream.Null, quic: quic);
        await using var dispatch = new Http3ResponseDispatch(quic,
            new Http3RecordingResponder(), new Http3Request([], null), new Http3HeaderBudget(options),
            options, failures.Add, input, CreateHeaders(options));

        dispatch.Start(TestContext.CancellationToken);
        await dispatch.Join(TestContext.CancellationToken);
        dispatch.Fail(new Http3ProtocolException(Http3Protocol.FrameUnexpected));

        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task A_header_deadline_after_the_final_response_preserves_the_completed_response()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        using var output = new MemoryStream();
        await using var source = new Http3PendingReadStream();
        var quic = new Http3RecordingQuicStream(output);
        var failures = new List<Exception>();
        var options = new Http3ServerOptions();
        await using var input = new Http3StreamReader(source, quic: quic);
        input.BeginBodyInspection(options.MaxBodyInspectionBytes);
        var read = input.ReadByte(lifetime.Token).AsTask();
        await source.WaitForRead(lifetime.Token);
        await using var dispatch = new Http3ResponseDispatch(quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, failures.Add, input, CreateHeaders(options));

        dispatch.Start(headers.Token);
        await dispatch.Join(lifetime.Token);
        await Assert.ThrowsExactlyAsync<Http3InspectionCompleteException>(() => read);
        var response = output.ToArray();
        await headers.CancelAsync();
        await dispatch.Join(lifetime.Token);

        Assert.IsNotEmpty(response);
        CollectionAssert.AreEqual(response, output.ToArray());
        Assert.AreEqual(1, quic.CompletedWrites);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
        Assert.IsNull(quic.WriteAbortCode);
        Assert.IsEmpty(failures);
    }

    private static async Task<bool> ObservePendingRead(Http3StreamReader reader, Http3ResponseDispatch dispatch,
        Http3InlineAbortQuicStream quic, CancellationToken cancellationToken)
    {
        try {
            await reader.ReadByte(cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (Http3InspectionCompleteException) {
            return quic.IsAbortingRead;
        }
        catch (Exception ex) {
            dispatch.Fail(ex);
            return false;
        }
    }

    private static Http3RequestHeaders CreateHeaders(Http3ServerOptions options, string method = "GET")
    {
        var headers = new Http3RequestHeaders(trailers: false, options.MaxRequestLineSize);
        headers.Append(new KeyValuePair<string, string>(":method", method));
        headers.Append(new KeyValuePair<string, string>(":scheme", "https"));
        headers.Append(new KeyValuePair<string, string>(":path", "/"));
        headers.Append(new KeyValuePair<string, string>(":authority", "example.com"));
        return headers;
    }

    private static async Task<string> ReadStatus(byte[] response, CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(response);
        await using var reader = new Http3StreamReader(source);
        Assert.AreEqual(1L, await reader.ReadInteger(cancellationToken));
        var length = await reader.ReadInteger(cancellationToken);
        var section = await Http3Qpack.ReadSection(reader, length, new Http3ServerOptions(), cancellationToken);
        return section.Fields.Single(field => field.Key == ":status").Value;
    }
}
