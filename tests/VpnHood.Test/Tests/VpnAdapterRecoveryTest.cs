using System.Diagnostics;
using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnAdapterRecoveryTest : TestBase
{
    private static VpnAdapterOptions CreateAdapterOptions(int? mtu = null)
    {
        return new VpnAdapterOptions {
            SessionName = null,
            VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.1/24"),
            IncludeNetworks = [IpNetwork.AllV4],
            DnsServers = [],
            Mtu = mtu
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
    public async Task Reader_that_outlived_its_stop_leaves_the_next_run()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        adapter.ReaderWaitTimeout = TimeSpan.FromMilliseconds(100);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // the reader is held past its stop's wait, and the next run starts beside it
        adapter.HoldReader = true;
        await AssertEqualsWait(true, () => adapter.IsReaderHeld);
        adapter.Stop();
        Assert.IsFalse(adapter.ReaderEndedAtClose);
        await adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(2, () => adapter.ReadingCount);

        // once released, the old reader finds its run retired and leaves; the new one reads on
        adapter.ReleaseReader();
        await AssertEqualsWait(1, () => adapter.ReadingCount);
        await Task.Delay(200, TestCt);
        Assert.AreEqual(1, adapter.ReadingCount);
        Assert.IsTrue(adapter.IsStarted);
    }

    [TestMethod]
    public async Task Start_overtaken_by_a_stop_starts_no_reader()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false) { StopAfterOpen = true };

        // the owner's stop lands between the start's open and its reader: the start reports the stop,
        // and no reader runs on the stopped adapter
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            adapter.Start(CreateAdapterOptions(), TestCt));
        await Task.Delay(200, TestCt);
        Assert.AreEqual(0, adapter.ReadingCount);
        Assert.IsFalse(adapter.IsStarted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Start_overtaken_before_its_add_removes_what_it_added(bool dispose)
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        adapter.StopBeforeAdd = !dispose;
        adapter.DisposeBeforeAdd = dispose;

        // the owner's stop or dispose lands before the start adds the adapter, so it finds nothing; the start
        // adds it anyway, as a step that ignores the token does, then ends before its routes and reports the
        // stop, and what it added goes with it: after a dispose no later start would clear it
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            adapter.Start(CreateAdapterOptions(), TestCt));
        Assert.AreEqual(0, adapter.RouteCount);
        Assert.AreEqual(0, adapter.OpenCount);
        Assert.IsFalse(adapter.IsAdded);
        Assert.AreEqual(0, adapter.ReadingCount);
    }

    [TestMethod]
    public async Task Start_overtaken_at_its_routes_opens_no_device()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false) { StopAtRoute = true };

        // the owner's stop lands in a route step that ignores the token: the start opens no device after it
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            adapter.Start(CreateAdapterOptions(), TestCt));
        Assert.AreEqual(0, adapter.OpenCount);
        Assert.IsFalse(adapter.IsAdded);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stop_ends_a_start_under_way_at_its_next_step(bool dispose)
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false) { HoldBeforeAdd = true };

        // the owner's stop or dispose cancels the start held before its add, which then adds nothing, rather
        // than adding the rest after the stop until its end finds it stopped
        var startTask = adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(true, () => adapter.IsStartHeld);
        if (dispose)
            adapter.Dispose();
        else
            adapter.Stop();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            startTask.WaitAsync(TimeSpan.FromSeconds(10), TestCt));
        Assert.IsFalse(adapter.IsAdded);
        Assert.AreEqual(0, adapter.OpenCount);
    }

    [TestMethod]
    public async Task Restart_leaves_a_start_under_way_alone()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: true) { HoldBeforeAdd = true };

        // an address change whose look began just before the owner's start fires a restart once that start has
        // marked the adapter started: the restart leaves it alone, where its stop would tear the start down
        var startTask = adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(true, () => adapter.IsStartHeld);
        var restartTask = adapter.Restart(TestCt);
        adapter.ReleaseStart();
        await restartTask;
        await startTask;
        Assert.IsTrue(adapter.IsStarted);
        Assert.AreEqual(1, adapter.StartCount);
    }

    [TestMethod]
    public async Task A_packet_sent_while_starting_does_not_wait_out_the_send_backoff()
    {
        // a backoff of ~10 s, which a slow machine cannot pass for
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false, maxPacketSendDelay: TimeSpan.FromSeconds(5)) {
            HoldBeforeAdd = true
        };

        // the start holds before its add, as a restart's does while it sets the device up: the adapter is
        // started, its device not open yet, so the write fails; the packet goes at once, as in a stop
        var startTask = adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(true, () => adapter.IsStartHeld);
        adapter.SendPacketQueued(BuildPacket());
        await AssertEqualsWait(1L, () => adapter.PacketStat.DroppedPackets, timeout: 2000);
        Assert.AreEqual(0L, adapter.PacketStat.SentPackets); // a drop is no packet sent

        adapter.ReleaseStart();
        await startTask;
        Assert.IsTrue(adapter.IsStarted);
    }

    [TestMethod]
    public async Task A_packet_the_interface_keeps_refusing_is_counted_as_dropped()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false, maxPacketSendDelay: TimeSpan.FromMilliseconds(20));
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // the write's retries run out and the packet is given up: a drop, not a packet sent
        adapter.RefuseWrite = true;
        adapter.SendPacketQueued(BuildPacket());
        await AssertEqualsWait(1L, () => adapter.PacketStat.DroppedPackets);
        Assert.AreEqual(0L, adapter.PacketStat.SentPackets);
        Assert.AreEqual(0L, adapter.PacketStat.SentBytes);

        // a packet the interface takes is counted as sent, as before
        adapter.RefuseWrite = false;
        var packet = BuildPacket();
        var packetLength = packet.PacketLength;
        adapter.SendPacketQueued(packet);
        await AssertEqualsWait(1L, () => adapter.PacketStat.SentPackets);
        Assert.AreEqual(packetLength, adapter.PacketStat.SentBytes);
        Assert.AreEqual(1L, adapter.PacketStat.DroppedPackets);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_Started_handler_may_stop_the_adapter_during_a_restart(bool dispose)
    {
        // not in a using: where the handler's stop waits for the restart that raised it, a dispose here would too
        var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);
        adapter.Started += (_, _) => {
            if (dispose)
                adapter.Dispose();
            else
                adapter.Stop();
        };

        // the restart ends, and the handler's stop with it
        await adapter.Restart(TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.IsFalse(adapter.IsStarted);
        adapter.Dispose();
    }

    [TestMethod]
    public async Task Started_is_raised_for_the_owners_start_and_for_each_restart()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        var startedCount = 0;
        var wasStarted = false;
        adapter.Started += (_, _) => {
            wasStarted = adapter.IsStarted;
            Interlocked.Increment(ref startedCount);
        };

        await adapter.Start(CreateAdapterOptions(), TestCt);
        Assert.AreEqual(1, startedCount);
        Assert.IsTrue(wasStarted);

        await adapter.Restart(TestCt);
        Assert.AreEqual(2, startedCount);
        Assert.IsTrue(wasStarted);
    }

    [TestMethod]
    public async Task A_Started_handler_that_starts_the_adapter_again_leaves_the_others_only_the_new_run()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        var firstOptions = CreateAdapterOptions();
        var secondOptions = CreateAdapterOptions();
        Task? secondStart = null;
        var heardOptions = new List<VpnAdapterOptions>(); // what the second subscriber is told, in order

        // the first subscriber answers the first run with a stop and a start of its own, which here completes
        // inside the handler, its own Started with it
        adapter.Started += (_, options) => {
            if (!ReferenceEquals(options, firstOptions))
                return;

            adapter.Stop();
            secondStart = adapter.Start(secondOptions, TestCt);
        };
        adapter.Started += (_, options) => heardOptions.Add(options);

        await adapter.Start(firstOptions, TestCt);
        Assert.IsNotNull(secondStart);
        await secondStart.WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        // the second subscriber hears of the run that is up, and not, after it, of the one that was over by its turn
        Assert.AreEqual(1, heardOptions.Count);
        Assert.AreSame(secondOptions, heardOptions[0]);
        Assert.IsTrue(adapter.IsStarted);
    }

    [TestMethod]
    public async Task A_failed_Started_handler_does_not_keep_the_notice_from_the_next()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        var startedCount = 0;
        adapter.Started += (_, _) => throw new InvalidOperationException("Test: the handler failed.");
        adapter.Started += (_, _) => startedCount++;

        // the failure is logged, not thrown out of the start
        await adapter.Start(CreateAdapterOptions(), TestCt);
        Assert.AreEqual(1, startedCount);
        Assert.IsTrue(adapter.IsStarted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_close_that_fails_does_not_skip_the_removal(bool dispose)
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);
        Assert.IsTrue(adapter.IsAdded);

        // the close fails and the interface goes anyway: after a dispose no start comes to remove a leftover
        adapter.FailClose = true;
        if (dispose)
            adapter.Dispose();
        else
            Assert.ThrowsExactly<IOException>(adapter.Stop);

        Assert.IsFalse(adapter.IsAdded);
        Assert.IsFalse(adapter.IsStarted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Start_overtaken_by_a_stop_leaves_no_run_state(bool dispose)
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        adapter.StopBeforeAdd = !dispose;
        adapter.DisposeBeforeAdd = dispose;

        // the stop clears the run's state, and the start, which runs on to its next check, writes some of it
        // again: the gateway of the address it adds. Its own cleanup clears that too
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            adapter.Start(CreateAdapterOptions(), TestCt));
        Assert.IsFalse(adapter.IsStarted);
        Assert.IsNull(adapter.AdapterIpNetworkV4);
        Assert.IsNull(adapter.GatewayIpV4);
    }

    [TestMethod]
    public async Task An_IPv6_address_the_interface_refuses_leaves_no_IPv6_gateway()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false) { FailAddIpV6 = true };
        var options = new VpnAdapterOptions {
            SessionName = null,
            VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.1/24"),
            VirtualIpNetworkV6 = IpNetwork.Parse("fd00::1/64"),
            IncludeNetworks = [IpNetwork.AllV4, IpNetwork.AllV6],
            DnsServers = []
        };

        // the run goes on without IPv6, and shows none of it
        await adapter.Start(options, TestCt);
        Assert.IsTrue(adapter.IsStarted);
        Assert.IsNotNull(adapter.GatewayIpV4);
        Assert.IsNull(adapter.AdapterIpNetworkV6);
        Assert.IsNull(adapter.GatewayIpV6);
    }

    [TestMethod]
    public async Task A_run_without_an_MTU_does_not_read_with_the_last_runs()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(mtu: 1400), TestCt);
        await AssertEqualsWait(1400, () => adapter.ReadBufferSize);

        // the next run sets no MTU, so its interface has the system's: the reader's buffer holds any packet again
        adapter.Stop();
        await adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(0xFFFF, () => adapter.ReadBufferSize);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [DoNotParallelize] // holds every thread of the process's pool for a moment
    public async Task Failed_is_not_raised_once_the_owner_has_stopped_the_adapter(bool dispose)
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        var failedCount = 0;
        adapter.Failed += (_, _) => Interlocked.Increment(ref failedCount);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // Failed is raised by a pool task, queued as the adapter stops on its own. The pool is held, so that task
        // still waits when the owner's stop begins: nothing here may await until the pool is released
        using var poolRelease = new ManualResetEventSlim(false);
        for (var i = 0; i < 2048; i++)
            ThreadPool.UnsafeQueueUserWorkItem(release => release.Wait(), poolRelease, preferLocal: false);

        var stopThread = new Thread(dispose ? adapter.Dispose : adapter.Stop);
        try {
            // ten read errors: the reader, on its own thread, stops the adapter and queues Failed. It queues it
            // before it ends, so its end tells that the task waits
            adapter.FailRead = true;
            Assert.IsTrue(SpinWait.SpinUntil(() => !adapter.IsStarted && Volatile.Read(ref adapter.ReadingCount) == 0,
                TimeSpan.FromSeconds(5)));

            // the owner's stop has begun once it waits, as it needs the pool too
            stopThread.Start();
            Assert.IsTrue(SpinWait.SpinUntil(
                () => stopThread.ThreadState.HasFlag(System.Threading.ThreadState.WaitSleepJoin),
                TimeSpan.FromSeconds(5)));
        }
        finally {
            poolRelease.Set();
        }

        Assert.IsTrue(stopThread.Join(TimeSpan.FromSeconds(10)));
        await WaitForThePoolsQueue();
        Assert.AreEqual(0, failedCount);
    }

    [TestMethod]
    [DoNotParallelize] // holds every thread of the process's pool for a moment
    public async Task Failed_of_a_run_is_not_raised_in_the_owners_next_run()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        var failedCount = 0;
        adapter.Failed += (_, _) => Interlocked.Increment(ref failedCount);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // as above: the first run's Failed waits in the held pool, and nothing here may await until it is released
        using var poolRelease = new ManualResetEventSlim(false);
        for (var i = 0; i < 2048; i++)
            ThreadPool.UnsafeQueueUserWorkItem(release => release.Wait(), poolRelease, preferLocal: false);

        Task restartTask;
        try {
            adapter.FailRead = true;
            Assert.IsTrue(SpinWait.SpinUntil(() => !adapter.IsStarted && Volatile.Read(ref adapter.ReadingCount) == 0,
                TimeSpan.FromSeconds(5)));

            // the owner starts it again, with no stop between; and that run is down for a moment, in a restart
            // of its own, when the first run's task gets its turn
            adapter.FailRead = false;
            var startTask = adapter.Start(CreateAdapterOptions(), TestCt);
            Assert.IsTrue(startTask.IsCompletedSuccessfully);
            restartTask = adapter.Restart(TestCt);
            Assert.IsFalse(restartTask.IsCompleted);
            Assert.IsFalse(adapter.IsStarted);
        }
        finally {
            poolRelease.Set();
        }

        // the owner of the run that is restarting never hears of the failure it answered with a new start
        await WaitForThePoolsQueue();
        Assert.AreEqual(0, failedCount);
        await restartTask.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.IsTrue(adapter.IsStarted);
        Assert.AreEqual(0, failedCount);
    }

    // what waited in the held pool has run: its queue is empty, and what it ran last has had its moment
    private async Task WaitForThePoolsQueue()
    {
        await AssertEqualsWait(0L, () => ThreadPool.PendingWorkItemCount);
        await Task.Delay(300, TestCt);
    }

    [TestMethod]
    public async Task A_null_adapters_reader_ends_with_its_run()
    {
        using var adapter = new ReaderCountingNullVpnAdapter();
        await adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(1, () => adapter.WaitCount);
        Assert.AreEqual(1, adapter.ReaderCount);

        // the reader waits for a packet that never comes: the stop releases it, where it stayed until disposal
        adapter.Stop();
        await AssertEqualsWait(0, () => adapter.ReaderCount);

        // the next run's reader waits again, rather than spinning on a wait already released
        await adapter.Start(CreateAdapterOptions(), TestCt);
        await AssertEqualsWait(2, () => adapter.WaitCount);
        await Task.Delay(200, TestCt);
        Assert.AreEqual(2, adapter.WaitCount);
        Assert.AreEqual(1, adapter.ReaderCount);
    }

    [TestMethod]
    public async Task A_null_adapter_disposes_a_packet_its_transport_leaves_to_it()
    {
        using var adapter = new NullVpnAdapter(autoDisposePackets: false, blocking: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // nothing takes a packet on from an adapter with no interface: it is the packet's last owner
        var packet = BuildPacket();
        adapter.SendPacketQueued(packet);
        await AssertEqualsWait(1L, () => adapter.PacketStat.SentPackets);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = packet.Buffer);
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
        // One packet per batch, as a batch ends at its first error: each sent before the next is queued
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
    public async Task Write_errors_stop_the_adapter_though_the_reader_polls_between_them()
    {
        using var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await adapter.Start(CreateAdapterOptions(), TestCt);

        // a write that keeps failing, slower than the reader's 20 ms polls: the reader's quiet polls
        // between the failures are no success of the sender's
        var ex = await WaitForFailed(adapter, async () => {
            adapter.FailWrite = true;
            for (var i = 0; i < 15 && adapter.IsStarted; i++) {
                adapter.SendPacketQueued(BuildPacket());
                await Task.Delay(60, TestCt);
            }
        });
        Assert.IsInstanceOfType<IOException>(ex);
        Assert.IsFalse(adapter.IsStarted);
    }

    [TestMethod]
    public async Task Client_ends_its_session_when_its_adapter_fails()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);

        var adapter = new TestFaultyVpnAdapter(autoRestart: false);
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: adapter);
        await client.WaitForState(ClientState.Connected, TestCt);

        // the adapter's restart fails, and the session ends with the error instead of staying connected
        adapter.FailStart = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => adapter.Restart(TestCt));
        await client.WaitForState(ClientState.Disposed, TestCt);
        var ex = client.LastException;
        Assert.IsInstanceOfType<VpnAdapterFailedException>(ex);
        Assert.IsInstanceOfType<IOException>(ex.InnerException);
        StringAssert.Contains(ex.Message, "adapter");
    }

    [TestMethod]
    public void A_rotated_temporary_IPv6_address_is_no_network_change()
    {
        IPAddress[] addresses = [
            IPAddress.Parse("192.168.1.10"), IPAddress.Parse("2001:db8:1:2:aaaa::1"), IPAddress.Parse("fe80::1%5")
        ];

        // the OS adds a temporary address within the prefix, and later drops the old one
        Assert.IsTrue(TunVpnAdapter.IsSameNetworks(addresses, [.. addresses, IPAddress.Parse("2001:db8:1:2:bbbb::2")]));
        Assert.IsTrue(TunVpnAdapter.IsSameNetworks(addresses, [
            IPAddress.Parse("192.168.1.10"), IPAddress.Parse("2001:db8:1:2:bbbb::2"), IPAddress.Parse("fe80::1%5")
        ]));

        // a new IPv4 address, or a new IPv6 prefix, is a new network
        Assert.IsFalse(TunVpnAdapter.IsSameNetworks(addresses, [
            IPAddress.Parse("192.168.1.11"), IPAddress.Parse("2001:db8:1:2:aaaa::1"), IPAddress.Parse("fe80::1%5")
        ]));
        Assert.IsFalse(TunVpnAdapter.IsSameNetworks(addresses, [
            IPAddress.Parse("192.168.1.10"), IPAddress.Parse("2001:db8:1:3:aaaa::1"), IPAddress.Parse("fe80::1%5")
        ]));
    }

    // a null adapter whose readers and their waits are counted
    private sealed class ReaderCountingNullVpnAdapter() : NullVpnAdapter(autoDisposePackets: true, blocking: false)
    {
        public int ReaderCount;
        public int WaitCount;

        protected override void StartReadingPackets()
        {
            Interlocked.Increment(ref ReaderCount);
            try {
                base.StartReadingPackets();
            }
            finally {
                Interlocked.Decrement(ref ReaderCount);
            }
        }

        protected override void WaitForTunRead()
        {
            Interlocked.Increment(ref WaitCount);
            base.WaitForTunRead();
        }
    }
}
