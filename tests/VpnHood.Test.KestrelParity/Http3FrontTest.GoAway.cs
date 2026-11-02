using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    [TestMethod]
    [TestCategory("Http3GoAway")]
    public async Task Client_goaway_closes_an_idle_connection_like_the_reference()
    {
        var expected = await ObserveClientGoAway(Servers.ReferenceEndPoint, TestContext.CancellationToken);
        TestContext.WriteLine($"Reference GOAWAY: close={expected.Close}, elapsed={expected.Elapsed}, " +
                              $"server identifiers=[{string.Join(",", expected.ServerGoAwayIds)}].");
        Assert.AreEqual(QuicError.ConnectionAborted, expected.Close.Error);
        Assert.AreEqual(0x100L, expected.Close.ApplicationErrorCode, "The reference must finish with H3_NO_ERROR.");

        var actual = await ObserveClientGoAway(Servers.OurEndPoint, TestContext.CancellationToken);
        TestContext.WriteLine($"Our GOAWAY: close={actual.Close}, elapsed={actual.Elapsed}, " +
                              $"server identifiers=[{string.Join(",", actual.ServerGoAwayIds)}].");
        Assert.AreEqual(expected.Close, actual.Close);
        AssertValidServerGoAwayIds(expected.ServerGoAwayIds, 0);
        AssertValidServerGoAwayIds(actual.ServerGoAwayIds, 0);
        // Allow scheduling variation, while rejecting an implementation that waits for the normal
        // 130-second idle timer instead of acting on GOAWAY. A probe deadline never counts as a close.
        Assert.IsTrue(actual.Elapsed <= expected.Elapsed + TimeSpan.FromSeconds(2),
            "The client-initiated graceful close was delayed.");
    }

    [TestMethod]
    [TestCategory("Http3GoAway")]
    [TestCategory("Http3Slow")]
    public async Task Client_goaway_drains_an_active_request_and_rejects_later_requests()
    {
        var keepAlive = new KestrelServerOptions().Limits.KeepAliveTimeout;
        var results = await Task.WhenAll(
            ObserveActiveClientGoAway(Servers.ReferenceEndPoint, keepAlive, requireAcceptWake: false,
                cancellationToken: TestContext.CancellationToken),
            ObserveActiveClientGoAway(Servers.OurEndPoint, keepAlive, requireAcceptWake: true,
                cancellationToken: TestContext.CancellationToken));
        var expected = results[0];
        TestContext.WriteLine($"Reference active GOAWAY: {expected}; " +
                              $"server identifiers=[{string.Join(",", expected.ServerGoAwayIds)}].");
        Assert.IsFalse(expected.ClosedBeforeOriginalCompleted, "GOAWAY must leave the accepted request alive.");
        Assert.AreEqual(QuicError.StreamAborted, expected.RejectedRequest.Error);
        Assert.AreEqual(0x10bL, expected.RejectedRequest.ApplicationErrorCode);
        Assert.IsTrue(expected.CompletedRequest.Fin);
        Assert.IsNotEmpty(expected.CompletedRequest.Bytes);
        Assert.AreEqual(QuicError.ConnectionAborted, expected.Close.Error);
        Assert.AreEqual(0x100L, expected.Close.ApplicationErrorCode);

        var actual = results[1];
        TestContext.WriteLine($"Our active GOAWAY: {actual}; " +
                              $"server identifiers=[{string.Join(",", actual.ServerGoAwayIds)}].");
        Assert.IsFalse(actual.ClosedBeforeOriginalCompleted, "GOAWAY closed an accepted request prematurely.");
        AssertSame("request rejected during GOAWAY", expected.RejectedRequest, actual.RejectedRequest);
        AssertSame("request completed during GOAWAY", expected.CompletedRequest, actual.CompletedRequest);
        Assert.AreEqual(expected.Close, actual.Close);
        AssertValidServerGoAwayIds(expected.ServerGoAwayIds, 8);
        AssertValidServerGoAwayIds(actual.ServerGoAwayIds, 8);
    }

    private async Task<ActiveGoAwayObservation> ObserveActiveClientGoAway(IPEndPoint endPoint,
        TimeSpan keepAlive, bool requireAcceptWake, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(keepAlive + Http3Probe.Timeout + Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var serverControl = await connection.AcceptInboundStreamAsync(deadline.Token);
        Assert.AreEqual(0UL, await ReadVariableInteger(serverControl, deadline.Token));
        Assert.AreEqual(4UL, await ReadVariableInteger(serverControl, deadline.Token));
        var settingsLength = await ReadVariableInteger(serverControl, deadline.Token);
        Assert.IsTrue(settingsLength <= 1024);
        await serverControl.ReadExactlyAsync(new byte[(int)settingsLength], deadline.Token);

        var headers = BuildQpackRequest([]);
        await using var original = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        await original.WriteAsync(headers.AsMemory(0, headers.Length - 1), deadline.Token);
        // Complete a later stream before GOAWAY so the first stream has already entered request handling.
        await using var barrier = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        await SendRequestTail(barrier, headers, finish: true, deadline.Token);
        var barrierResponse = await ReadCompleteResponse(barrier, deadline.Token);
        Assert.IsTrue(barrierResponse.Fin);
        Assert.IsNotEmpty(barrierResponse.Bytes);

        var frames = ReadServerGoAwayFrames(serverControl, deadline.Token);
        var close = ObserveGoAwayConnectionClose(connection, deadline.Token);
        try {
            await control.WriteAsync(new byte[] { 7, 1, 0 }, deadline.Token);
            // Control and request streams are scheduled independently. This only gives GOAWAY time to
            // arrive; success still requires a positive rejection, a complete response, and a peer close.
            await Task.Delay(TimeSpan.FromMilliseconds(150), deadline.Token);
            await using var later = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
            await SendRequestTail(later, headers, finish: true, deadline.Token);
            var rejectedRequest = await ReadCompleteResponse(later, deadline.Token);
            var closedBeforeOriginalCompleted = close.IsCompleted;

            await SendRequestTail(original, headers[^1..], finish: true, deadline.Token);
            var completedRequest = await ReadCompleteResponse(original, deadline.Token);
            TestContext.WriteLine($"Active GOAWAY at {endPoint}: rejected={rejectedRequest}; " +
                                  $"completed={completedRequest}; closed early={closedBeforeOriginalCompleted}.");
            // The reference may close after the accepted request drains, or wait for an accept-loop
            // wake. Require our connection to keep the drain alive until that wake in either case.
            var drainedAt = Stopwatch.GetTimestamp();
            var observation = Task.Delay(keepAlive + TimeSpan.FromSeconds(2), deadline.Token);
            var first = await Task.WhenAny(close, observation);
            deadline.Token.ThrowIfCancellationRequested();
            if (requireAcceptWake)
                Assert.AreSame(observation, first,
                    $"{endPoint}: drained connection closed without another accept: {DescribeClose(close)}.");
            if (first == observation)
                await observation;
            var idleElapsed = Stopwatch.GetElapsedTime(drainedAt);
            if (requireAcceptWake)
                Assert.IsFalse(close.IsCompleted, $"{endPoint}: drained connection closed at keep-alive.");
            if (!close.IsCompleted) {
                Assert.IsFalse(frames.IsCompleted, $"{endPoint}: server control stream ended during the drain.");
                await WakeGoAwayAcceptLoop(connection, deadline.Token);
            }
            await Task.WhenAll(close, frames);
            return new ActiveGoAwayObservation(rejectedRequest, completedRequest, await close, await frames,
                closedBeforeOriginalCompleted, idleElapsed);
        }
        finally {
            // A failed request assertion or deadline must not leave either observer reading a disposed stream.
            await deadline.CancelAsync();
            try {
                await Task.WhenAll(close, frames);
            }
            catch (OperationCanceledException) {
                // The operation that failed above remains the test failure; cancellation only joins observers.
            }
        }
    }

    private static string DescribeClose(Task<Http3ProbeResult> close) =>
        close.IsCompletedSuccessfully ? close.Result.ToString() : close.Status.ToString();

    private static async Task WakeGoAwayAcceptLoop(QuicConnection connection, CancellationToken cancellationToken)
    {
        try {
            await using var wake = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional,
                cancellationToken);
            await wake.WriteAsync(new byte[] { 0x21 }, cancellationToken);
            wake.CompleteWrites();
        }
        catch (QuicException) {
            // The connection observer verifies the close code even when close overtakes this stream.
        }
    }

    private static void AssertValidServerGoAwayIds(IReadOnlyList<ulong> identifiers, ulong expectedIdentifier)
    {
        // Repeated reference runs delivered either the final frame or only the QUIC close. If the
        // frame arrives, it must name the next stream after the highest request accepted before GOAWAY.
        foreach (var identifier in identifiers)
            Assert.AreEqual(expectedIdentifier, identifier, "The final GOAWAY identifier must exclude later requests.");
    }

    private static async Task<GoAwayObservation> ObserveClientGoAway(IPEndPoint endPoint,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var serverControl = await connection.AcceptInboundStreamAsync(deadline.Token);
        Assert.AreEqual(0UL, await ReadVariableInteger(serverControl, deadline.Token));
        Assert.AreEqual(4UL, await ReadVariableInteger(serverControl, deadline.Token));
        var settingsLength = await ReadVariableInteger(serverControl, deadline.Token);
        Assert.IsTrue(settingsLength <= 1024);
        await serverControl.ReadExactlyAsync(new byte[(int)settingsLength], deadline.Token);

        // Start both observers before sending GOAWAY. Keeping every critical stream open avoids
        // generating a close ourselves, and observing connection acceptance distinguishes reset from close.
        var frames = ReadServerGoAwayFrames(serverControl, deadline.Token);
        var close = ObserveGoAwayConnectionClose(connection, deadline.Token);
        var started = Stopwatch.GetTimestamp();
        await control.WriteAsync(new byte[] { 7, 1, 0 }, deadline.Token);
        // Observe both tasks even if the peer never closes; neither failed read is left behind at disposal.
        await Task.WhenAll(close, frames);
        var result = await close;
        var elapsed = Stopwatch.GetElapsedTime(started);
        return new GoAwayObservation(result, await frames, elapsed);
    }

    private static async Task<IReadOnlyList<ulong>> ReadServerGoAwayFrames(QuicStream stream,
        CancellationToken cancellationToken)
    {
        var identifiers = new List<ulong>();
        try {
            while (true) {
                var type = await ReadVariableInteger(stream, cancellationToken);
                var length = await ReadVariableInteger(stream, cancellationToken);
                Assert.IsTrue(length <= 1024, "Unexpectedly large server control frame during graceful close.");
                var payload = new byte[(int)length];
                await stream.ReadExactlyAsync(payload, cancellationToken);
                if (type != 7)
                    continue;
                using var integer = new MemoryStream(payload);
                identifiers.Add(await ReadVariableInteger(integer, cancellationToken));
                Assert.AreEqual(integer.Length, integer.Position, "GOAWAY must contain exactly one identifier.");
            }
        }
        catch (QuicException) {
            // The separate connection observer records the authoritative close scope and code.
        }
        catch (EndOfStreamException) {
            // A graceful control FIN also ends this observer; it is not sufficient to pass the close test.
        }
        return identifiers.ToArray();
    }

    private static async Task<Http3ProbeResult> ObserveGoAwayConnectionClose(QuicConnection connection,
        CancellationToken cancellationToken)
    {
        var extraStreams = new List<QuicStream>();
        try {
            while (true)
                extraStreams.Add(await connection.AcceptInboundStreamAsync(cancellationToken));
        }
        catch (QuicException ex) {
            return new Http3ProbeResult("", false, ex.QuicError, ex.ApplicationErrorCode);
        }
        finally {
            foreach (var stream in extraStreams)
                await stream.DisposeAsync();
        }
    }
}
