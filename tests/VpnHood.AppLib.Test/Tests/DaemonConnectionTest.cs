using System.Diagnostics;
using System.Net;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Test.Daemon;
using VpnHood.AppUi.Hosting.Cli;
using VpnHood.AppUi.Hosting.Cli.Channel;
using VpnHood.AppUi.Hosting.Cli.Exceptions;

namespace VpnHood.AppLib.Test.Tests;

// The channel's line protocol and the connection that follows it, with the platform's half stood in for.
[TestClass]
public class DaemonConnectionTest
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static DaemonChannelAnswer Answer(int port, string token)
    {
        return new DaemonChannelAnswer {
            ApiUrl = new Uri($"http://127.0.0.1:{port}/?nocache=1{LocalApiToken.Fragment(token)}"),
            ProcessId = Environment.ProcessId,
            Version = "1.2.3"
        };
    }

    private static async Task<DaemonChannelServer> Serve(TestDaemonChannel channel, DaemonChannelAnswer answer)
    {
        return await DaemonChannelServer.TryStart(channel, answer, "Only administrators may use the test app.", CancellationToken.None)
               ?? throw new InvalidOperationException("The channel could not be opened.");
    }

    private static Task<DaemonConnection> Open(TestDaemonChannel channel, TestLoopbackPeerCheck? peerCheck = null,
        TestInstanceController? instance = null)
    {
        return DaemonConnection.Open(channel, peerCheck ?? new TestLoopbackPeerCheck(),
            instance ?? new TestInstanceController(), new TestCliPaths(), CancellationToken.None);
    }

    private static async Task WaitForChange(DaemonConnection connection, Func<Task> change)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ApiUrlProvider.Changed += (_, _) => changed.TrySetResult();
        await change();
        await changed.Task.WaitAsync(Wait);
    }

    [TestMethod]
    public async Task Channel_answers_an_administrator_and_refuses_anyone_else()
    {
        var channel = new TestDaemonChannel();
        await using var server = await Serve(channel, Answer(4711, "t1"));

        var answer = await DaemonChannelClient.AskOnce(channel, CancellationToken.None);
        Assert.IsNull(answer.Refusal);
        Assert.AreEqual(4711, answer.ApiUrl?.Port);
        Assert.AreEqual("t1", answer.Token);
        Assert.AreEqual(Environment.ProcessId, answer.ProcessId);
        Assert.AreEqual("1.2.3", answer.Version);

        channel.IsAdministrator = false;
        var refused = await DaemonChannelClient.AskOnce(channel, CancellationToken.None);
        Assert.IsNull(refused.ApiUrl, "a refused caller learns no address");
        Assert.AreEqual("Only administrators may use the test app.", refused.Refusal);

        await server.DisposeAsync();
        server.Publish(Answer(4712, "t2")); // a rebind as the service stops: nothing to tell, and no error
        await Assert.ThrowsAsync<Exception>(() => DaemonChannelClient.AskOnce(channel, CancellationToken.None),
            "nothing serves it now");
    }

    [TestMethod]
    public async Task A_caller_that_says_nothing_is_closed_after_a_few_seconds()
    {
        var channel = new TestDaemonChannel();
        await using var server = await Serve(channel, Answer(4711, "t1"));

        await using var stream = await channel.Connect(CancellationToken.None);
        var elapsed = Stopwatch.StartNew();
        Assert.IsTrue(await IsClosed(stream, TimeSpan.FromSeconds(15)), "the service closed it");
        Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromSeconds(4), $"closed after {elapsed.Elapsed}, before its time");
    }

    [TestMethod]
    public async Task A_first_line_that_does_not_end_is_cut_off()
    {
        var channel = new TestDaemonChannel();
        await using var server = await Serve(channel, Answer(4711, "t1"));

        await using var stream = await channel.Connect(CancellationToken.None);
        await stream.WriteAsync(Enumerable.Repeat((byte)'a', 5000).ToArray());
        Assert.IsTrue(await IsClosed(stream, TimeSpan.FromSeconds(2)), "closed long before the silent caller's time");
    }

    [TestMethod]
    public async Task A_failed_accept_does_not_close_the_channel()
    {
        var channel = new TestDaemonChannel { FailNextAccept = true };
        await using var server = await Serve(channel, Answer(4711, "t1"));

        await Assert.ThrowsAsync<Exception>(() => DaemonChannelClient.AskOnce(channel, CancellationToken.None),
            "the caller whose accept failed is lost");

        var answer = await DaemonChannelClient.AskOnce(channel, CancellationToken.None).WaitAsync(Wait);
        Assert.AreEqual(4711, answer.ApiUrl?.Port, "the next is answered");
    }

    [TestMethod]
    public async Task The_same_address_again_is_no_change()
    {
        var channel = new TestDaemonChannel();
        await using var server = await Serve(channel, Answer(4711, "t1"));
        await using var connection = await Open(channel);

        var changes = 0;
        connection.ApiUrlProvider.Changed += (_, _) => Interlocked.Increment(ref changes);
        server.Publish(Answer(4711, "t1"));
        await WaitForChange(connection, () => {
            server.Publish(Answer(4711, "t2"));
            return Task.CompletedTask;
        });

        // lines arrive in order, so one for the same address would have come first
        Assert.AreEqual(1, changes, "only the new token is a change");
    }

    [TestMethod]
    public async Task Open_refuses_at_once_and_says_when_nothing_runs()
    {
        var channel = new TestDaemonChannel();
        var instance = new TestInstanceController { Running = false };
        var notRunning = await Assert.ThrowsExactlyAsync<DaemonNotRunningException>(() => Open(channel, instance: instance));
        Assert.AreEqual(instance.NotRunningHint, notRunning.Message);

        await using var server = await Serve(channel, Answer(4711, "t1"));
        channel.IsAdministrator = false;
        var refused = await Assert.ThrowsExactlyAsync<DaemonRefusedException>(() => Open(channel));
        Assert.AreEqual("Only administrators may use the test app.", refused.Message);
    }

    [TestMethod]
    public async Task Connection_follows_the_address_the_channel_sends()
    {
        var channel = new TestDaemonChannel();
        await using var server = await Serve(channel, Answer(4711, "t1"));
        await using var connection = await Open(channel);
        Assert.AreEqual(4711, connection.ApiUrlProvider.Current.Port);
        Assert.AreEqual("t1", LocalApiToken.Read(connection.ApiUrlProvider.Current));

        // the listener moved: everyone connected hears of it
        await WaitForChange(connection, () => {
            server.Publish(Answer(4712, "t2"));
            return Task.CompletedTask;
        });
        Assert.AreEqual(4712, connection.ApiUrlProvider.Current.Port);
        Assert.AreEqual("t2", LocalApiToken.Read(connection.ApiUrlProvider.Current));

        // the service went and came back: the connection asks again by itself
        await server.DisposeAsync();
        await using var restarted = await Serve(channel, Answer(4713, "t3"));
        await WaitForChange(connection, () => Task.CompletedTask);
        Assert.AreEqual(4713, connection.ApiUrlProvider.Current.Port);
    }

    [TestMethod]
    public async Task Calls_go_to_the_latest_address_with_the_latest_token()
    {
        using var first = new TestHttpServer(_ => (HttpStatusCode.OK, "first"));
        using var second = new TestHttpServer(_ => (HttpStatusCode.OK, "second"));
        var channel = new TestDaemonChannel();
        await using var server = await Serve(channel, Answer(first.Port, "t1"));
        await using var connection = await Open(channel);

        Assert.AreEqual("first", await connection.Api.App.Log(CancellationToken.None));
        Assert.IsTrue(first.Requests.TryDequeue(out var request));
        Assert.AreEqual("/api/app/log.txt", request.Target);
        Assert.AreEqual("Bearer t1", request.Authorization);
        Assert.AreEqual($"127.0.0.1:{first.Port}", request.Host);

        await WaitForChange(connection, () => {
            server.Publish(Answer(second.Port, "t2"));
            return Task.CompletedTask;
        });
        Assert.AreEqual("second", await connection.Api.App.Log(CancellationToken.None));
        Assert.IsTrue(second.Requests.TryDequeue(out request));
        Assert.AreEqual("Bearer t2", request.Authorization);
        Assert.AreEqual(0, first.Requests.Count, "nothing goes to the old address");
    }

    [TestMethod]
    public async Task A_refused_call_is_sent_again_with_the_next_token()
    {
        var channel = new TestDaemonChannel();
        DaemonChannelServer? server = null;
        var port = 0;
        using var api = new TestHttpServer(request => {
            if (request.Authorization == "Bearer t2")
                return (HttpStatusCode.OK, "ok");

            // the listener bound again a moment before the channel's line: it arrives now
            server?.Publish(Answer(port, "t2"));
            return (HttpStatusCode.Unauthorized, "");
        });

        port = api.Port;
        server = await Serve(channel, Answer(api.Port, "t1"));
        await using (server) {
            await using var connection = await Open(channel);
            Assert.AreEqual("ok", await connection.Api.App.Log(CancellationToken.None));
            Assert.AreEqual(2, api.Requests.Count, "once refused, once more with the new token");
            Assert.IsTrue(api.Requests.TryDequeue(out var refused) && refused.Authorization == "Bearer t1");
            Assert.IsTrue(api.Requests.TryDequeue(out var retried) && retried.Authorization == "Bearer t2");
        }
    }

    [TestMethod]
    public async Task A_connection_to_another_process_carries_nothing()
    {
        using var api = new TestHttpServer(_ => (HttpStatusCode.OK, "ok"));
        var channel = new TestDaemonChannel();
        await using var server = await Serve(channel, Answer(api.Port, "t1"));
        var peerCheck = new TestLoopbackPeerCheck { Answer = (remote, _) => remote.Port != api.Port };
        await using var connection = await Open(channel, peerCheck);

        var error = await Assert.ThrowsAsync<Exception>(() => connection.Api.App.Log(CancellationToken.None));
        var messages = string.Join(" | ", Unwrap(error).Select(x => x.Message));
        StringAssert.Contains(messages, "held by another program");
        await Task.Delay(200);
        Assert.AreEqual(0, api.Requests.Count, "the request never went out");

        // the process the channel named is what the check is given
        peerCheck.Answer = (_, processId) => processId == Environment.ProcessId;
        Assert.AreEqual("ok", await connection.Api.App.Log(CancellationToken.None));
    }

    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            yield return current;
    }

    // Whether the other end closed the connection in time, by an end of stream or a reset.
    private static async Task<bool> IsClosed(Stream stream, TimeSpan timeout)
    {
        try {
            return await stream.ReadAsync(new byte[1]).AsTask().WaitAsync(timeout) == 0;
        }
        catch (IOException) {
            return true;
        }
        catch (TimeoutException) {
            return false;
        }
    }
}
