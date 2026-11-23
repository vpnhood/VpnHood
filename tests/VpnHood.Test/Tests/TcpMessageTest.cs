using System.Text;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Test.Tests;

[TestClass]
public class TcpMessageTest
{
    public required TestContext TestContext { get; set; }
    private CancellationToken TestCt => TestContext.CancellationToken;

    [TestMethod]
    public async Task Client_should_reconnect_after_listener_restart()
    {
        var configFolder = CreateConfigFolder();
        try {
            using var client = new TcpMessageClient(configFolder);

            using (var listener = new TcpMessageListener(configFolder)) {
                var listenerTask = listener.Start(Echo, TestCt);
                await AssertEcho(client, "first");
                listener.Dispose();
                await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            }

            using (var listener = new TcpMessageListener(configFolder)) {
                var listenerTask = listener.Start(Echo, TestCt);
                await AssertEcho(client, "second");
                listener.Dispose();
                await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            }
        }
        finally {
            Directory.Delete(configFolder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Client_should_preserve_caller_cancellation()
    {
        var configFolder = CreateConfigFolder();
        try {
            using var listener = new TcpMessageListener(configFolder);
            using var client = new TcpMessageClient(configFolder);
            using var requestCts = new CancellationTokenSource();
            var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var listenerTask = listener.Start(async (_, cancellationToken) => {
                requestReceived.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Memory<byte>.Empty;
            }, TestCt);

            var sendTask = client.SendAsync(new byte[] { 1 }, requestCts.Token);
            await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), requestCts.Token);
            await requestCts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => sendTask);

            listener.Dispose();
            // requestCts has already been canceled; the timeout is the only guard for the listener unwind
            // ReSharper disable once MethodSupportsCancellation
            await listenerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally {
            Directory.Delete(configFolder, recursive: true);
        }
    }

    // A connect that ran out of its own time read as a cut connection, a cancellation inside, where a request that runs
    // out of its time has a TimeoutException inside. It reads as a timeout too
    [TestMethod]
    public async Task A_connect_that_runs_out_of_time_reads_as_a_timeout()
    {
        var configFolder = CreateConfigFolder();
        try {
            using var listener = new TcpMessageListener(configFolder);
            var listenerTask = listener.Start(Echo, TestCt);
            using var client = new TcpMessageClient(configFolder) { ConnectTimeout = TimeSpan.Zero };

            var ex = await Assert.ThrowsAsync<VpnServiceUnreachableException>(() =>
                client.SendAsync(new byte[] { 1 }, TestCt));

            Assert.IsInstanceOfType<TimeoutException>(ex.InnerException);

            listener.Dispose();
            await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        }
        finally {
            Directory.Delete(configFolder, recursive: true);
        }
    }

    // A request the service took was sent again on a new connection where the kept one failed before its answer, so a
    // reconfigure or a rewarded ad's data could run twice. Only a request that did not leave is sent again
    [TestMethod]
    public async Task A_request_the_service_took_is_not_sent_again()
    {
        var configFolder = CreateConfigFolder();
        try {
            using var listener = new TcpMessageListener(configFolder);
            using var client = new TcpMessageClient(configFolder);
            var requestCount = 0;

            // the second request's connection fails once the service took it, with no answer
            var listenerTask = listener.Start((request, _) => Interlocked.Increment(ref requestCount) == 2
                ? throw new IOException("The connection failed after the request was taken.")
                : Task.FromResult(request), TestCt);

            await AssertEcho(client, "first");
            await Assert.ThrowsExactlyAsync<VpnServiceUnreachableException>(() =>
                client.SendAsync(new byte[] { 2 }, TestCt));

            Assert.AreEqual(2, requestCount, "A request the service took was sent again.");
            listener.Dispose();
            await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        }
        finally {
            Directory.Delete(configFolder, recursive: true);
        }
    }

    // A send that waited for another's turn went on after the client's close, and put a connection in place that nothing
    // closed then. It is refused
    [TestMethod]
    public async Task A_send_that_waited_for_its_turn_is_refused_after_the_close()
    {
        var configFolder = CreateConfigFolder();
        try {
            using var listener = new TcpMessageListener(configFolder);
            var client = new TcpMessageClient(configFolder);
            var firstTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var listenerTask = listener.Start(async (request, cancellationToken) => {
                firstTaken.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return request;
            }, TestCt);

            // the first send holds the turn, and the second waits for it
            var first = client.SendAsync(new byte[] { 1 }, TestCt);
            await firstTaken.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            var second = client.SendAsync(new byte[] { 2 }, TestCt);

            client.Dispose();
            release.TrySetResult();
            await Assert.ThrowsExactlyAsync<VpnServiceUnreachableException>(() => first);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => second);

            listener.Dispose();
            await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        }
        finally {
            Directory.Delete(configFolder, recursive: true);
        }
    }

    private static Task<Memory<byte>> Echo(Memory<byte> request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    private async Task AssertEcho(TcpMessageClient client, string message)
    {
        var request = Encoding.UTF8.GetBytes(message);
        var response = await client.SendAsync(request, TestCt);
        CollectionAssert.AreEqual(request, response.ToArray());
    }

    private static string CreateConfigFolder()
    {
        var path = Path.Combine(TestHelper.AssemblyWorkingPath, nameof(TcpMessageTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
