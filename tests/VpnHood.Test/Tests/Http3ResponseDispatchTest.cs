using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3ResponseDispatchTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public void Selecting_again_returns_the_frozen_limit_result()
    {
        using var output = new MemoryStream();
        var options = new Http3ServerOptions { MaxHeaderCount = 1 };
        var budget = new Http3HeaderBudget(options);
        var failures = new List<Exception>();
        var dispatch = new Http3ResponseDispatch(output, new Http3RecordingQuicStream(),
            new Http3RecordingResponder(), new Http3Request([], null), budget, options, failures.Add);
        budget.Append(new KeyValuePair<string, string>("x", ""));

        Assert.IsFalse(dispatch.Select());
        budget.Append(new KeyValuePair<string, string>("x", ""));

        Assert.IsTrue(budget.ExceedsRequestLimits);
        Assert.IsFalse(dispatch.Select());
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task A_protocol_failure_prevents_the_response_and_precedes_caller_cancellation()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream();
        var responder = new Http3RecordingResponder();
        var options = new Http3ServerOptions();
        var failures = new List<Exception>();
        var dispatch = new Http3ResponseDispatch(output, quic, responder, new Http3Request([], null),
            new Http3HeaderBudget(options), options, failures.Add);
        var failure = new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);

        dispatch.Fail(failure);
        await dispatch.Complete(lifetime.Token);
        await lifetime.CancelAsync();
        dispatch.Fail(new OperationCanceledException(lifetime.Token));
        var actual = Assert.ThrowsExactly<Http3ProtocolException>(() => dispatch.ThrowIfFailed(lifetime.Token));

        Assert.AreSame(failure, actual);
        Assert.AreSame(failure, failures.Single());
        Assert.AreEqual(0L, output.Length);
        Assert.IsNull(responder.Request);
        Assert.AreEqual(0, quic.CompletedWrites);
    }

    [TestMethod]
    public async Task A_canceled_response_keeps_awaiting_its_write_before_reporting_failure()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var failures = new List<Exception>();
        using var output = new Http3PendingWriteStream(lifetime.Token);
        var quic = new Http3RecordingQuicStream();
        var options = new Http3ServerOptions();
        var dispatch = new Http3ResponseDispatch(output, quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, failures.Add);
        var response = dispatch.Complete(headers.Token);
        try {
            await output.WaitForWrite(lifetime.Token);
            await headers.CancelAsync();
            await output.WaitForCancellation(lifetime.Token);

            Assert.IsFalse(response.IsCompleted, "Cancellation must not abandon the native write.");
            Assert.IsEmpty(failures);
            output.ReleaseWrite();
            await response.WaitAsync(lifetime.Token);
            var actual = Assert.ThrowsExactly<OperationCanceledException>(() => dispatch.ThrowIfFailed(lifetime.Token));

            Assert.AreSame(actual, failures.Single());
            Assert.AreEqual(headers.Token, actual.CancellationToken);
            Assert.AreEqual(0, quic.CompletedWrites);
        }
        finally {
            output.ReleaseWrite();
            try {
                await response.WaitAsync(lifetime.Token);
            }
            catch (Exception) {
                // Cleanup still observes the write before disposing its stream.
            }
        }
    }

    [TestMethod]
    public async Task A_header_deadline_after_the_final_response_preserves_the_completed_response()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream();
        var failures = new List<Exception>();
        var options = new Http3ServerOptions();
        var dispatch = new Http3ResponseDispatch(output, quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, failures.Add);

        await dispatch.Complete(headers.Token);
        var response = output.ToArray();
        await headers.CancelAsync();
        await dispatch.Complete(headers.Token);
        dispatch.ThrowIfFailed(lifetime.Token);

        Assert.IsNotEmpty(response);
        CollectionAssert.AreEqual(response, output.ToArray());
        Assert.AreEqual(1, quic.CompletedWrites);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
        Assert.IsNull(quic.WriteAbortCode);
        Assert.IsEmpty(failures);
    }
}
