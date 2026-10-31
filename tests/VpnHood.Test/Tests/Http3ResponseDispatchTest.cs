using System.Collections.Concurrent;
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
        using var dispatch = new Http3ResponseDispatch(output, new Http3RecordingQuicStream(),
            new Http3RecordingResponder(), new Http3Request([], null), budget, options, failures.Add,
            TestContext.CancellationToken);
        budget.Append(new KeyValuePair<string, string>("x", ""));

        Assert.IsFalse(dispatch.Select());
        budget.Append(new KeyValuePair<string, string>("x", ""));

        Assert.IsTrue(budget.ExceedsRequestLimits);
        Assert.IsFalse(dispatch.Select());
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task A_protocol_failure_is_reported_before_cancellation_and_joins_the_pending_write()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        var events = new ConcurrentQueue<string>();
        var failures = new ConcurrentQueue<Exception>();
        using var output = new Http3PendingWriteStream(() => events.Enqueue("canceled"), lifetime.Token);
        var quic = new Http3RecordingQuicStream();
        var options = new Http3ServerOptions();
        using var dispatch = new Http3ResponseDispatch(output, quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, error => {
                events.Enqueue("reported");
                failures.Enqueue(error);
            }, lifetime.Token);
        var failure = new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);
        var response = dispatch.Complete(lifetime.Token);
        try {
            await output.WaitForWrite(lifetime.Token);
            dispatch.Fail(failure);
            await output.WaitForCancellation(lifetime.Token);

            var join = dispatch.Join(lifetime.Token);
            Assert.IsFalse(join.IsCompleted, "Reporting an error must not abandon the native write.");
            output.ReleaseWrite();
            var actual = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() => join.WaitAsync(lifetime.Token));
            await response.WaitAsync(lifetime.Token);

            Assert.AreSame(failure, actual);
            Assert.AreSame(failure, failures.Single());
            CollectionAssert.AreEqual(new[] { "reported", "canceled" }, events.ToArray());
            Assert.AreEqual(0, quic.CompletedWrites);
        }
        finally {
            output.ReleaseWrite();
            try {
                await response.WaitAsync(lifetime.Token);
                await dispatch.Join(lifetime.Token).WaitAsync(lifetime.Token);
            }
            catch (Exception) {
                // The assertions retain the failure; cleanup still observes dispatch before disposing its stream.
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
        var failures = new ConcurrentQueue<Exception>();
        var options = new Http3ServerOptions();
        using var dispatch = new Http3ResponseDispatch(output, quic, new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, failures.Enqueue, headers.Token);

        await dispatch.Complete(headers.Token);
        var response = output.ToArray();
        await headers.CancelAsync();
        await dispatch.Complete(headers.Token);
        await dispatch.Join(lifetime.Token);

        Assert.IsNotEmpty(response);
        CollectionAssert.AreEqual(response, output.ToArray());
        Assert.AreEqual(1, quic.CompletedWrites);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
        Assert.IsNull(quic.WriteAbortCode);
        Assert.IsEmpty(failures);
    }
}
