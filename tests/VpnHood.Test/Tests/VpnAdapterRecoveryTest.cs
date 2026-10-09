using System.Diagnostics;
using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnAdapterRecoveryTest : TestBase
{
    private static VpnAdapterOptions CreateAdapterOptions()
    {
        return new VpnAdapterOptions {
            SessionName = null,
            VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.1/24"),
            IncludeNetworks = [IpNetwork.AllV4],
            DnsServers = []
        };
    }

    private static IpPacket BuildPacket()
    {
        return PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), 1000, 53, new byte[16]);
    }

    private static async Task<Exception> WaitForFailed(TestFaultyVpnAdapter adapter, Func<Task> action)
    {
        var failed = new TaskCompletionSource<Exception>();
        adapter.Failed += (_, ex) => failed.TrySetResult(ex);
        await action();
        return await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task Failed_restart_raises_Failed_and_leaves_the_adapter_stopped()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);
        Assert.IsTrue(adapter.IsStarted);

        // the restart's start fails, so the owner hears it and the adapter stays down
        adapter.FailStart = true;
        var ex = await WaitForFailed(adapter, () =>
            Assert.ThrowsExactlyAsync<IOException>(() => adapter.Restart(TestCt)));
        Assert.IsInstanceOfType<IOException>(ex);
        Assert.IsFalse(adapter.IsStarted);

        // no retry without AutoRestart
        var startCount = adapter.StartCount;
        await Task.Delay(1000, TestCt);
        Assert.AreEqual(startCount, adapter.StartCount);
    }

    [TestMethod]
    public async Task Failed_restart_is_retried_until_it_starts()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: true);
        var failedCount = 0;
        adapter.Failed += (_, _) => failedCount++;
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // the restart's start fails, and the adapter retries by itself once the fault is gone
        adapter.FailStart = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => adapter.Restart(TestCt));
        Assert.IsFalse(adapter.IsStarted);
        await AssertEqualsWait(true, () => adapter.StartCount >= 4, timeout: 10000); // two retries: it keeps trying
        adapter.FailStart = false;
        await AssertEqualsWait(2, () => adapter.OpenCount, timeout: 10000);
        Assert.IsTrue(adapter.IsStarted);
        Assert.AreEqual(0, failedCount);
    }

    [TestMethod]
    public async Task Stop_ends_the_retry()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: true);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        adapter.FailStart = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => adapter.Restart(TestCt));
        adapter.Stop();
        adapter.FailStart = false;

        // no retry after the owner's stop, and no restart either
        var startCount = adapter.StartCount;
        await Task.Delay(1000, TestCt);
        await adapter.Restart(TestCt);
        Assert.AreEqual(startCount, adapter.StartCount);
        Assert.IsFalse(adapter.IsStarted);
    }

    [TestMethod]
    public async Task Stop_during_a_restart_leaves_the_adapter_stopped()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        var failedCount = 0;
        adapter.Failed += (_, _) => failedCount++;
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // the owner's stop lands in the restart's pause: the restart ends, nothing starts it again, and
        // the owner is not told of a stop it asked for (a client's disconnect during a network change)
        var restartTask = adapter.Restart(TestCt);
        await AssertEqualsWait(false, () => adapter.IsStarted);
        adapter.Stop();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => restartTask);
        await Task.Delay(1000, TestCt);
        Assert.IsFalse(adapter.IsStarted);
        Assert.AreEqual(1, adapter.OpenCount);
        Assert.AreEqual(0, failedCount);
    }

    [TestMethod]
    public async Task Owner_failed_start_is_not_retried()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: true);
        var failedCount = 0;
        adapter.Failed += (_, _) => failedCount++;

        // the caller has the exception: nothing to keep started
        adapter.FailStart = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => adapter.Start(CreateAdapterOptions(), TestCt));
        adapter.FailStart = false;
        await Task.Delay(1000, TestCt);
        Assert.AreEqual(1, adapter.StartCount);
        Assert.IsFalse(adapter.IsStarted);
        Assert.AreEqual(0, failedCount);
    }

    [TestMethod]
    public async Task Read_errors_stop_the_adapter_and_raise_Failed()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        var ex = await WaitForFailed(adapter, () => {
            adapter.FailRead = true;
            return Task.CompletedTask;
        });
        Assert.IsInstanceOfType<IOException>(ex);
        Assert.IsFalse(adapter.IsStarted);
    }

    [TestMethod]
    public async Task Read_errors_restart_the_adapter()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: true);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // the reader's errors stop the adapter; the retry brings it back once they are gone
        adapter.FailRead = true;
        await AssertEqualsWait(false, () => adapter.IsStarted);
        adapter.FailRead = false;
        await AssertEqualsWait(2, () => adapter.OpenCount, timeout: 10000);
        Assert.IsTrue(adapter.IsStarted);
        Assert.AreEqual(2, adapter.StartCount);
    }

    [TestMethod]
    public async Task Stop_waits_for_the_reader()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        adapter.HoldReader = true;
        await adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(true, () => adapter.IsReaderHeld);

        // the stop waits while the reader is held, and ends once the reader has left
        var stopTask = Task.Run(adapter.Stop, TestCt);
        await Task.Delay(500, TestCt);
        Assert.IsFalse(stopTask.IsCompleted);
        adapter.ReleaseReader();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsTrue(adapter.ReaderEndedAtClose);
        Assert.IsFalse(adapter.WasReadingAtClose);
        Assert.IsFalse(adapter.IsStarted);
    }

    [TestMethod]
    public async Task Reader_stop_does_not_wait_for_itself()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(true, () => adapter.IsReading);

        // the read errors stop the adapter on the reader's thread: at once, not after the wait's 5 s
        adapter.FailRead = true;
        var stopwatch = Stopwatch.StartNew();
        await AssertEqualsWait(false, () => adapter.IsStarted, timeout: 3000);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.IsTrue(adapter.ReaderEndedAtClose);
        Assert.IsTrue(adapter.WasReadingAtClose);
    }

    [TestMethod]
    public async Task Stop_waits_for_the_sender()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // a packet whose write is held: the stop waits for its batch, and ends once the batch has
        adapter.HoldSender = true;
        adapter.SendPacketQueued(BuildPacket());
        await AssertEqualsWait(true, () => adapter.IsSenderHeld);
        var stopTask = Task.Run(adapter.Stop, TestCt);
        await Task.Delay(500, TestCt);
        Assert.IsFalse(stopTask.IsCompleted);
        adapter.ReleaseSender();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsTrue(adapter.SenderEndedAtClose);
        Assert.IsFalse(adapter.IsStarted);
    }

    [TestMethod]
    public async Task Sender_stop_does_not_wait_for_itself()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // the write errors stop the adapter on the sender's thread: at once, not after the wait's 5 s.
        // One packet per batch, as a batch ends at its first error, and fast: the reader's polling
        // resets the error count every 20 ms, so the spin never sleeps (a sleep lasts a whole timer tick)
        adapter.FailWrite = true;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 30 && adapter.IsStarted; i++) {
            adapter.SendPacketQueued(BuildPacket());
            var spinWait = new SpinWait();
            while (adapter.QueueLength > 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(3)) // a stalled sender fails, not hangs
                spinWait.SpinOnce(sleep1Threshold: -1);
        }
        await AssertEqualsWait(false, () => adapter.IsStarted, timeout: 3000);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.IsTrue(adapter.SenderEndedAtClose);
    }

    [TestMethod]
    public async Task Client_ends_its_session_when_its_adapter_fails()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: adapter);
        await client.WaitForState(ClientState.Connected);

        // the adapter's restart fails, and the session ends with the error instead of staying connected
        adapter.FailStart = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => adapter.Restart(TestCt));
        await client.WaitForState(ClientState.Disposed);
        var ex = client.LastException;
        Assert.IsInstanceOfType<InvalidOperationException>(ex);
        Assert.IsInstanceOfType<IOException>(ex.InnerException);
        StringAssert.Contains(ex.Message, "adapter");
    }
}
