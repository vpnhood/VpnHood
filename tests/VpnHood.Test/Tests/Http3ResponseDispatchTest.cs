using System.Collections.Concurrent;
using System.Net;
using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3ResponseDispatchTest
{
    public required TestContext TestContext { get; set; }

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
        using var dispatch = new Http3ResponseDispatch(output, quic, CreateContext(), new Http3RecordingResponder(),
            new Http3Request([], null), new Http3HeaderBudget(options), options, error => {
                events.Enqueue("reported");
                failures.Enqueue(error);
            }, lifetime.Token);
        var failure = new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);
        try {
            await dispatch.Start(lifetime.Token);
            await output.WaitForWrite(lifetime.Token);
            dispatch.Fail(failure);
            await output.WaitForCancellation(lifetime.Token);

            var join = dispatch.Join(lifetime.Token);
            Assert.IsFalse(join.IsCompleted, "Reporting an error must not abandon the native write.");
            output.ReleaseWrite();
            var actual = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() => join.WaitAsync(lifetime.Token));

            Assert.AreSame(failure, actual);
            Assert.AreSame(failure, failures.Single());
            CollectionAssert.AreEqual(new[] { "reported", "canceled" }, events.ToArray());
            Assert.AreEqual(0, quic.CompletedWrites);
        }
        finally {
            output.ReleaseWrite();
            try { await dispatch.Join(lifetime.Token).WaitAsync(lifetime.Token); }
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
        using var dispatch = new Http3ResponseDispatch(output, quic, CreateContext(), new Http3RecordingResponder(),
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

    private static Http3StreamContext CreateContext() =>
        new(new IPEndPoint(IPAddress.Loopback, 0), new IPEndPoint(IPAddress.Loopback, 0));
}
