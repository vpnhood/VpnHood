using System.Text.Json;
using System.Threading.Channels;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Test.Device;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnServiceManagerTest : TestBase
{
    [TestMethod]
    public async Task Reading_the_status_file_does_not_fail_the_services_write()
    {
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // while the service disconnects, the manager learns its state from the file alone
        var statusJson = JsonSerializer.Serialize(new ConnectionInfo {
            ClientState = ClientState.Disconnecting,
            CreatedTime = DateTime.UtcNow,
            ClientStateChangedTime = null,
            ClientStateProgress = null,
            ProxyConnectorStatus = null,
            SessionInfo = null,
            SessionStatus = null,
            Error = null
        });

        // the manager reads as fast as it can
        using var readCts = new CancellationTokenSource();
        var readTask = Task.Run(async () => {
            while (!readCts.IsCancellationRequested)
                await manager.RefreshState(CancellationToken.None);
        }, TestCt);

        // the service writes its state as VpnServiceContext does: a write a reader kept out throws, and the
        // service would lose that state
        try {
            for (var i = 0; i < 1000; i++)
                await File.WriteAllTextAsync(statusFilePath, statusJson, TestCt);
        }
        finally {
            await readCts.CancelAsync();
            await readTask;
        }

        // a read that caught the file half-written was passed over: the manager has what was written
        await manager.RefreshState(TestCt);
        Assert.AreEqual(ClientState.Disconnecting, manager.ConnectionInfo.ClientState);
    }

    [TestMethod]
    public async Task A_cancelled_start_that_ends_late_leaves_the_newer_start_its_state()
    {
        // each start waits at the VPN permission's ask until the test answers, and only then sees its cancel
        var asks = Channel.CreateUnbounded<TaskCompletionSource>();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            RequestVpnServiceHandler = async cancellationToken => {
                var answer = new TaskCompletionSource();
                await asks.Writer.WriteAsync(answer, CancellationToken.None);
                await answer.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var serviceOptions = new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions() };

        // the first start is cancelled while it waits there
        using var firstCts = new CancellationTokenSource();
        var firstStart = manager.Start(uiContext: null, serviceOptions, firstCts.Token);
        var firstAnswer = await asks.Reader.ReadAsync(TestCt);
        await firstCts.CancelAsync();

        // the newer start's stop gives up on it; the newer one writes Initializing and waits there too
        using var secondCts = new CancellationTokenSource();
        var secondStart = manager.Start(uiContext: null, serviceOptions, secondCts.Token);
        var secondAnswer = await asks.Reader.ReadAsync(TestCt);

        // the first ends now and leaves the state to the newer one
        firstAnswer.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => firstStart);
        Assert.AreEqual(ClientState.Initializing, manager.ConnectionInfo.ClientState);
        Assert.IsNull(manager.ConnectionInfo.Error);

        // the newer start's end is its own to write
        await secondCts.CancelAsync();
        secondAnswer.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => secondStart);
        Assert.AreEqual(ClientState.Disposed, manager.ConnectionInfo.ClientState);
    }
}
